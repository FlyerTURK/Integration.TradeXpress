using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// İç graf doğrulayıcısının (<see cref="GraphValidator"/>) sözleşme kilidi: bağlam-yollu etiketleme
/// (<c>Entity:X + Code → ... → alan: mesaj</c>), soft-delete atlama ve "kökün kendi scalar'ları dışarıda"
/// kapsam sınırı. Bunlar sessizce bozulursa kullanıcı yine bağlamsız "Name alanı zorunludur." çağına döner.
///
/// <para><b>Test grafı neden Integration.* namespace'inde:</b> gezgin yalnız <c>Integration.</c> köklü
/// tiplere iner (BCL'e dalmama konvansiyonu) — buradaki private DTO'lar bu dosyanın namespace'i sayesinde
/// kapsama girer; başka namespace'e taşınırlarsa testler sessizce "temiz graf"a döner, taşıma.</para>
///
/// <para><b>Assert'ler pass-through localizer'a göre:</b> her anahtar "bulundu" sayılır ve aynen döner
/// (<c>BlazorComponentTestBase</c>'in sahtesiyle aynı semantik — o private olduğundan burada kopyası var).
/// Yani beklenen çıktıda <c>Entity:TestCompany</c> / <c>DisplayName:Address</c> / <c>Validation:Required</c>
/// anahtarları ham hâliyle görünür; çeviri doğruluğu bu testin işi değil (LocalizationParityTests).</para>
/// </summary>
public class GraphValidatorTests
{
    private static readonly IStringLocalizer Localizer = new PassThroughLocalizer();

    [Fact]
    public void Collection_item_violation_is_labelled_with_entity_key_and_code()
    {
        var root = new Root
        {
            Title = "Kök",
            Companies = new List<TestCompany>
            {
                new TestCompany { Name = "", Code = "FMS" },
            },
        };

        var errors = GraphValidator.Validate(root, Localizer);

        errors.ShouldHaveSingleItem().ShouldBe("Entity:TestCompany FMS: Validation:Required");
    }

    [Fact]
    public void Soft_deleted_item_is_skipped()
    {
        // Silinmiş satırın ihlali kullanıcıyı kilitleyemez — Name boş olsa bile rapor edilmez.
        var root = new Root
        {
            Title = "Kök",
            Companies = new List<TestCompany>
            {
                new TestCompany { Name = null, Code = "FMS", IsDeleted = true },
            },
        };

        GraphValidator.Validate(root, Localizer).ShouldBeEmpty();
    }

    [Fact]
    public void Nested_address_violation_carries_full_parent_path()
    {
        var root = new Root
        {
            Title = "Kök",
            Companies = new List<TestCompany>
            {
                new TestCompany
                {
                    Name = "Acme",
                    Code = "FMS",
                    Branches = new List<TestBranch>
                    {
                        new TestBranch { Code = "HQ", Address = new TestAddress { City = null } },
                    },
                },
            },
        };

        var errors = GraphValidator.Validate(root, Localizer);

        // Koleksiyon elemanları entity etiketi + Code ile, tek-nesne çocuk (Address) property adının
        // çevirisiyle; parçalar " → " ile zincirlenir — kullanıcı HANGİ şubenin adresi olduğunu görür.
        errors.ShouldHaveSingleItem().ShouldBe(
            "Entity:TestCompany FMS → Entity:TestBranch HQ → DisplayName:Address: Validation:Required");
    }

    [Fact]
    public void Clean_graph_returns_no_errors()
    {
        var root = new Root
        {
            Title = "Kök",
            Companies = new List<TestCompany>
            {
                new TestCompany
                {
                    Name = "Acme",
                    Code = "FMS",
                    Branches = new List<TestBranch>
                    {
                        new TestBranch { Code = "HQ", Address = new TestAddress { City = "İstanbul" } },
                    },
                },
            },
        };

        GraphValidator.Validate(root, Localizer).ShouldBeEmpty();
    }

    [Fact]
    public void Root_own_scalar_violations_are_excluded()
    {
        // Kökün scalar'larını üst-düzey validator inline duyurur (çift bildirim olmasın) — gezginin
        // kapsam sınırı bu. Kök Title ihlali listeye GİRMEZ; aynı grafta çocuk ihlali yine yakalanır
        // (yani "hiç çalışmadı" değil, "kökü bilerek atladı" ispatlanır).
        var root = new Root
        {
            Title = "",
            Companies = new List<TestCompany>
            {
                new TestCompany { Name = "", Code = "FMS" },
            },
        };

        var errors = GraphValidator.Validate(root, Localizer);

        errors.ShouldHaveSingleItem().ShouldBe("Entity:TestCompany FMS: Validation:Required");
    }

    #region Test grafı (yalnız bu testlerin taşıyıcısı — üretim DTO'su değildir)

    private sealed class Root
    {
        // Fact: kök scalar ihlali listeye girmemeli — bunun için bilerek [Required].
        [Required]
        public string? Title { get; set; }

        public List<TestCompany> Companies { get; set; } = new();
    }

    private sealed class TestCompany
    {
        [Required]
        public string? Name { get; set; }

        public string? Code { get; set; }

        public bool IsDeleted { get; set; }

        public List<TestBranch> Branches { get; set; } = new();
    }

    private sealed class TestBranch
    {
        [Required]
        public string? Code { get; set; }

        public TestAddress? Address { get; set; }
    }

    private sealed class TestAddress
    {
        [Required]
        public string? City { get; set; }
    }

    #endregion

    /// <summary>Her anahtarı bulundu sayıp aynen döndürür — <c>BlazorComponentTestBase</c>'teki private
    /// sahtenin buradaki eşleniği (format argümanları dahil aynı <c>string.Format</c> semantiği).</summary>
    private sealed class PassThroughLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get
            {
                return new LocalizedString(name, name, resourceNotFound: false);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                return new LocalizedString(name, string.Format(name, arguments), resourceNotFound: false);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Array.Empty<LocalizedString>();
        }
    }
}
