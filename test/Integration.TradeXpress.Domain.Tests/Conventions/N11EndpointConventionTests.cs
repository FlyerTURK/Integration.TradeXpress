using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// N11 endpoint adresinin kaynağa GERİ SIZMASINI engelleyen konvansiyon testi.
///
/// <para><b>Neden var:</b> <c>https://api.n11.com</c> dokuz ayrı istemcide sabit gömülüydü ve tek bir
/// yapılandırılabilir tabana çekildi (<c>N11EndpointOptions</c>). Sebep estetik değil işlevsel: N11 hesap
/// erişimi kapalıyken (2026-08-05: iki ayrı gerçek hesap da <c>401 "erişiminiz durdurulmuştur"</c>) N11 kodunu
/// denemenin TEK yolu istekleri N11 gibi konuşan yerel bir sunucuya yönlendirmektir. Yeni bir istemci adresi
/// yeniden gömerse o istemci sahte sunucuyu ATLAR — ve bu sessizce olur: kod derlenir, testler geçer, yalnız
/// o uç gerçek N11'e gitmeye devam eder ve hesap kapalı olduğu için 401 alır.</para>
///
/// <para><b>İstisna yok.</b> Adrese ihtiyaç duyan her yer <c>N11EndpointOptions</c>'tan okur. Tek meşru
/// literal, options tipinin KENDİ varsayılanıdır (allow-list'te).</para>
/// </summary>
public class N11EndpointConventionTests
{
    /// <summary>Varsayılan adresi tanımlamaya HAK KAZANMIŞ tek dosya — options tipinin kendisi.</summary>
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/Integration.TradeXpress.Application/N11Products/N11EndpointOptions.cs",
    };

    [Fact]
    public void N11_host_must_not_be_hardcoded_outside_the_options_type()
    {
        var violations = new List<string>();

        foreach (var file in ConventionSource.EnumerateSource("*.cs"))
        {
            var relative = ConventionSource.RelativePath(file);
            if (AllowedFiles.Contains(relative))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("api.n11.com", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Yorum satırındaki geçiş bilgi amaçlıdır (ör. "varsayılan https://api.n11.com") — kod değil.
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add(
                    $"{relative}:{i + 1}: N11 konağı KODA GÖMÜLMÜŞ. Adres N11EndpointOptions'tan okunmalı — " +
                    "aksi hâlde bu uç yerel sahte sunucuyu atlar ve hesap kapalıyken 401 alır.");
            }
        }

        violations.ShouldBeEmpty(
            "Aşağıdaki dosyalar N11 uç adresini sabit yazıyor:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
