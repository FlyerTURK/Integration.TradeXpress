using System;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Satışa hazırlık panelinin "Düzelt →" isteği — nereye (hangi sekme/form) ve hangi kayda (varyant / kanal ürünü).
/// UI-içi taşıyıcı: sunucu sözleşmesi (<see cref="SaleReadinessFixTarget"/> + hedef id) iki parçayı ayrı
/// taşır; tek EventCallback'te yan yana durması için burada birleşir.</summary>
public sealed record SaleReadinessNavigation(SaleReadinessFixTarget Target, Guid? TargetId);
