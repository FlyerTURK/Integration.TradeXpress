using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

var path = "E:\\Kodlarim\\Yeni\\src\\Integration.TradeXpress.Domain.Shared\\Localization\\TradeXpress\\tr.json";
var json = File.ReadAllText(path);
var doc = JsonNode.Parse(json);
var texts = doc["Texts"].AsObject();

texts["Enum:ProcessDirectionType:Inbound:UI"] = "GIRIS";
texts["Enum:ProcessDirectionType:Outbound:UI"] = "CIKIS";
texts["Enum:ProcessPaymentType:Normal:UI"] = "NORMAL";
texts["Enum:ProcessPaymentType:WithCash:UI"] = "PESIN";
texts["Enum:ProcessPaymentType:WithCurrency:UI"] = "BEDELLI";
texts["Enum:ProcessPaymentType:Return:UI"] = "IADE";
texts["Enum:ProcessPaymentType:Consignment:UI"] = "EMANET";
texts["Enum:ProcessPaymentType:Reservation:UI"] = "REZERVASYON";

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(path, doc.ToJsonString(options));

var path2 = "E:\\Kodlarim\\Yeni\\src\\Integration.TradeXpress.Domain.Shared\\Localization\\TradeXpress\\en.json";
var json2 = File.ReadAllText(path2);
var doc2 = JsonNode.Parse(json2);
var texts2 = doc2["Texts"].AsObject();

texts2["Enum:ProcessDirectionType:Inbound:UI"] = "INBOUND";
texts2["Enum:ProcessDirectionType:Outbound:UI"] = "OUTBOUND";
texts2["Enum:ProcessPaymentType:Normal:UI"] = "NORMAL";
texts2["Enum:ProcessPaymentType:WithCash:UI"] = "PREPAID";
texts2["Enum:ProcessPaymentType:WithCurrency:UI"] = "WITH CURRENCY";
texts2["Enum:ProcessPaymentType:Return:UI"] = "RETURN";
texts2["Enum:ProcessPaymentType:Consignment:UI"] = "CONSIGNMENT";
texts2["Enum:ProcessPaymentType:Reservation:UI"] = "RESERVATION";

File.WriteAllText(path2, doc2.ToJsonString(options));
