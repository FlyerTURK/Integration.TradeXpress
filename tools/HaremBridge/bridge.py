import json, time, threading, subprocess, os, queue
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from curl_cffi import requests
from curl_cffi.requests.websockets import CurlWsFlag

WS_URL = 'wss://hrmsocketonly.haremaltin.com/socket.io/?EIO=4&transport=websocket'
HARVEST_INTERVAL = 3600 * 2     # cf_clearance proaktif yenileme (tahmini omur; OLCULMELI)
FRAME_DEAD_SECS  = 55           # hic frame gelmezse olu baglanti (ping ~25sn'de bir)
PRICE_STALE_SECS = 120          # ping akar ama fiyat gelmezse bayat
WATCHDOG_EVERY   = 5
PORT             = int(os.environ.get('PORT', '8765'))
BASE_DIR         = os.path.dirname(os.path.abspath(__file__))
HARVEST_FILE     = os.path.join(BASE_DIR, 'harvest.json')

def log(m): print(f"{datetime.now().strftime('%H:%M:%S')} {m}", flush=True)

state_lock = threading.Lock()
state = {"source": "haremaltin", "lastUpdate": None, "ageSeconds": 0, "stale": True, "count": 0, "kurlar": {}}
conn_lock = threading.Lock()
mon = {"ws": None, "active": False, "last_frame": 0.0, "last_price": 0.0}

# SSE aboneleri: her /events istemcisi kendi Queue'sunu alir; update_prices her
# price_changed DELTA'sini tum kuyruklara basar. Dolu kuyruk = olu/yavas istemci.
subs_lock = threading.Lock()
subscribers = set()

def publish_event(delta_payload):
    dead = []
    with subs_lock:
        for q in subscribers:
            try: q.put_nowait(delta_payload)
            except queue.Full: dead.append(q)
        for q in dead: subscribers.discard(q)

def do_harvest():
    log("[HARVEST] node harvest.js...")
    subprocess.run(["node", "harvest.js"], cwd=BASE_DIR, check=True)
    log("[HARVEST] Tamam.")

def update_prices(payload_str):
    try:
        data = json.loads(payload_str)
        if isinstance(data, list) and len(data) > 1 and data[0] == "price_changed":
            prices = data[1].get("data", {})
            if prices:
                now_iso = datetime.now(timezone.utc).isoformat()
                with state_lock:
                    state["kurlar"].update(prices); state["lastUpdate"] = now_iso
                    state["count"] = len(state["kurlar"])
                # SSE: yalniz degisen semboller (delta) push edilir.
                publish_event(json.dumps(
                    {"lastUpdate": now_iso, "kurlar": prices}, ensure_ascii=False))
                return True
    except Exception as e: log(f"[WS] parse hatasi: {e}")
    return False

def _cleanup(ws, s):
    for o in (ws, s):
        try:
            if o is not None: o.close()
        except Exception: pass

def _safe_remove(p):
    try:
        if os.path.exists(p): os.remove(p)
    except Exception: pass

def watchdog_func():
    while True:
        time.sleep(WATCHDOG_EVERY)
        ws = reason = None
        with conn_lock:
            if not mon["active"] or mon["ws"] is None: continue
            now = time.time()
            if now - mon["last_frame"] > FRAME_DEAD_SECS:
                reason = f"{int(now-mon['last_frame'])}sn frame yok"
            elif now - mon["last_price"] > PRICE_STALE_SECS:
                reason = f"{int(now-mon['last_price'])}sn fiyat yok (bayat)"
            if reason: ws = mon["ws"]; mon["active"] = False
        if ws is not None:
            log(f"[WATCHDOG] {reason} → WS kapatiliyor")
            try: ws.close()
            except Exception: pass

def ws_thread_func():
    while True:
        try:
            if (not os.path.exists(HARVEST_FILE)) or \
               (time.time() - os.path.getmtime(HARVEST_FILE) > HARVEST_INTERVAL):
                try: do_harvest()
                except Exception as e: log(f"[WS] Harvest basarisiz: {e}"); time.sleep(10); continue
            harvest_time = os.path.getmtime(HARVEST_FILE)
            with open(HARVEST_FILE, encoding='utf-8') as f: h = json.load(f)
            headers = {
                'User-Agent': h.get('ua', ''),
                'Origin': 'https://canlipiyasalar.haremaltin.com',
                'Referer': 'https://canlipiyasalar.haremaltin.com/',
                'Accept-Language': 'tr-TR,tr;q=0.9',
                'Cookie': h.get('cookieHeader', ''),
            }
            log("[WS] Baglaniliyor..."); s = ws = None
            try:
                s = requests.Session(impersonate='chrome')
                ws = s.ws_connect(WS_URL, headers=headers, timeout=20)
                log("[WS] Baglandi!")
            except Exception as e:
                log(f"[WS] Baglanti hatasi: {e}"); _cleanup(ws, s)
                if "429" in str(e): time.sleep(30)
                elif any(c in str(e) for c in ("403","401","503")): _safe_remove(HARVEST_FILE)
                else: time.sleep(5)
                continue
            def recv_txt():
                msg = ws.recv(); d = msg[0] if isinstance(msg, tuple) else msg
                return d.decode('utf-8','ignore') if isinstance(d,(bytes,bytearray)) else str(d)
            try:
                if not recv_txt().startswith('0'): _cleanup(ws, s); continue
                ws.send('40', flags=CurlWsFlag.TEXT)   # KRITIK: TEXT frame
                now = time.time()
                with conn_lock: mon.update(ws=ws, active=True, last_frame=now, last_price=now)
                while True:
                    if time.time() - harvest_time > HARVEST_INTERVAL:
                        log("[WS] cf_clearance yas siniri → re-harvest"); break
                    txt = recv_txt()
                    with conn_lock: mon["last_frame"] = time.time()
                    if not txt: log("[WS] sunucu kapatti"); break
                    if txt == '2': ws.send('3', flags=CurlWsFlag.TEXT)
                    elif txt.startswith('42'):
                        if update_prices(txt[txt.find('['):]):
                            with conn_lock: mon["last_price"] = time.time()
            except Exception as e: log(f"[WS] dinleme sonlandi: {str(e)[:80]}")
            finally:
                with conn_lock: mon["active"] = False; mon["ws"] = None
                _cleanup(ws, s)
            log("[WS] 3sn sonra reconnect..."); time.sleep(3)
        except Exception as e: log(f"[WS] dongu hatasi: {str(e)[:120]}"); time.sleep(5)

class H(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/':
            with state_lock:
                if state["lastUpdate"]:
                    age = int((datetime.now(timezone.utc) - datetime.fromisoformat(state["lastUpdate"])).total_seconds())
                    state["ageSeconds"] = age; state["stale"] = age > PRICE_STALE_SECS
                body = json.dumps(state, ensure_ascii=False)
            self.send_response(200)
            self.send_header('Content-type', 'application/json; charset=utf-8')
            self.send_header('Access-Control-Allow-Origin', '*'); self.end_headers()
            self.wfile.write(body.encode('utf-8'))
        elif self.path == '/events':
            # SSE: ilk event TAM snapshot, sonrasi price_changed DELTA'lari.
            # Once abone ol, sonra snapshot'i al — arada gelen delta hem
            # snapshot'ta hem kuyrukta olabilir; merge idempotent, sorun degil.
            q = queue.Queue(maxsize=500)
            with subs_lock: subscribers.add(q)
            try:
                with state_lock:
                    snapshot = json.dumps(
                        {"lastUpdate": state["lastUpdate"], "kurlar": state["kurlar"]},
                        ensure_ascii=False)
                self.send_response(200)
                self.send_header('Content-Type', 'text/event-stream; charset=utf-8')
                self.send_header('Cache-Control', 'no-cache')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(f"data: {snapshot}\n\n".encode('utf-8')); self.wfile.flush()
                while True:
                    try: payload = q.get(timeout=15)
                    except queue.Empty:
                        # Sessiz piyasada baglanti canliligi: yorum-satiri ping.
                        self.wfile.write(b": ping\n\n"); self.wfile.flush(); continue
                    self.wfile.write(f"data: {payload}\n\n".encode('utf-8')); self.wfile.flush()
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError, OSError):
                pass
            finally:
                with subs_lock: subscribers.discard(q)
        else: self.send_response(404); self.end_headers()
    def log_message(self, *a): pass

if __name__ == '__main__':
    threading.Thread(target=ws_thread_func, daemon=True).start()
    threading.Thread(target=watchdog_func, daemon=True).start()
    # ThreadingHTTPServer sart: /events uzun omurlu baglantilar tutar; tek
    # is parcacikli HTTPServer'da bir SSE istemcisi tum sunucuyu kilitlerdi.
    httpd = ThreadingHTTPServer(('127.0.0.1', PORT), H)
    httpd.daemon_threads = True
    log(f"Server started at http://127.0.0.1:{PORT}")
    try: httpd.serve_forever()
    except KeyboardInterrupt: log("Kapatiliyor...")
