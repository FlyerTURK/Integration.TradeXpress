using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// <see cref="ProductCategoryTreeManager.CollectSubtreeIds"/> mekanik ağı (DB'siz).
///
/// <para><b>Neden bu testler var (2026-07-27 Hakan):</b> "üst kategoriler alt kategorilerinden üst kategori
/// seçemesin, circular oluşur." Bu küme "üst kategori" combo'sundan DÜŞÜLENLERİ belirler; eksik hesaplanırsa
/// kullanıcı kendi torununu üst seçebilir ve sunucu guard'ının fırlattığı hatayla karşılaşır.</para>
/// </summary>
public class ProductCategorySubtreeTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GrandChild = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid GreatGrandChild = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Sibling = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid OtherRoot = Guid.Parse("00000000-0000-0000-0000-000000000006");

    /// <summary>Root → Child → GrandChild → GreatGrandChild; ayrıca Root'un ikinci çocuğu Sibling ve ayrık OtherRoot.</summary>
    private static List<(Guid Id, Guid? ParentId)> BuildTree()
    {
        return new List<(Guid, Guid?)>
        {
            (Root, null),
            (Child, Root),
            (GrandChild, Child),
            (GreatGrandChild, GrandChild),
            (Sibling, Root),
            (OtherRoot, null),
        };
    }

    [Fact]
    public void Subtree_contains_the_node_itself()
    {
        // Kategori kendi ebeveyni olamaz → kendisi de dışlanacaklar arasında olmalı.
        ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), Child).ShouldContain(Child);
    }

    [Fact]
    public void Subtree_reaches_every_level_below_not_just_direct_children()
    {
        // YALNIZ doğrudan çocukları toplasaydı torun seçilebilir kalır ve döngü kurulurdu.
        var subtree = ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), Root);

        subtree.ShouldBe(new HashSet<Guid> { Root, Child, GrandChild, GreatGrandChild, Sibling }, ignoreOrder: true);
    }

    [Fact]
    public void Subtree_excludes_siblings_and_unrelated_branches()
    {
        // Kardeş ve ayrık kök MEŞRU üst adaylarıdır — gereksiz dışlamak kullanıcıyı engellerdi.
        var subtree = ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), Child);

        subtree.ShouldBe(new HashSet<Guid> { Child, GrandChild, GreatGrandChild }, ignoreOrder: true);
        subtree.ShouldNotContain(Sibling);
        subtree.ShouldNotContain(OtherRoot);
        subtree.ShouldNotContain(Root);   // kendi ATASI üst olarak zaten seçilidir/seçilebilir
    }

    [Fact]
    public void Leaf_subtree_is_only_itself()
    {
        ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), GreatGrandChild)
            .ShouldBe(new HashSet<Guid> { GreatGrandChild }, ignoreOrder: true);
    }

    [Fact]
    public void Unknown_id_yields_only_that_id()
    {
        // Yeni (henüz kaydedilmemiş) kayıt ya da başka şirketin id'si: kimseyi dışlamamalı.
        var unknown = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

        ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), unknown)
            .ShouldBe(new HashSet<Guid> { unknown }, ignoreOrder: true);
    }

    [Fact]
    public void Inactive_node_in_the_middle_does_not_break_the_chain()
    {
        // Asıl sebep bu: picker yalnız AKTİF kategorileri döndürür. Zincirin ortasındaki düğüm pasifse
        // istemci torunu göremezdi; hesap TAM ağaç üzerinden yapıldığı için GrandChild yine dışlanır.
        // (Burada "pasiflik" ağaç verisinde görünmez — test, hesabın aktiflikten BAĞIMSIZ olduğunu sabitler:
        //  girdi tüm düğümleri içerdiği sürece sonuç eksiksizdir.)
        var subtree = ProductCategoryTreeManager.CollectSubtreeIds(BuildTree(), Root);

        subtree.ShouldContain(GrandChild);
        subtree.ShouldContain(GreatGrandChild);
    }

    [Fact]
    public void Corrupt_cycle_in_data_does_not_hang_the_walk()
    {
        // Elle düzenlenmiş DB'de A→B→A oluşabilir. Guard'ın kendisi sonsuz dönerse hiçbir kategori formu
        // açılamazdı; ziyaret işareti yürüyüşü keser.
        var cyclic = new List<(Guid, Guid?)>
        {
            (Root, GrandChild),   // kök, kendi torununu ebeveyn gösteriyor (bozuk)
            (Child, Root),
            (GrandChild, Child),
        };

        var subtree = ProductCategoryTreeManager.CollectSubtreeIds(cyclic, Root);

        subtree.ShouldBe(new HashSet<Guid> { Root, Child, GrandChild }, ignoreOrder: true);
    }
}
