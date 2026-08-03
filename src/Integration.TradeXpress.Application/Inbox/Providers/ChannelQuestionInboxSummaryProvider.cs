using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.TradeXpress.ChannelQuestions;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.Extensions.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Inbox.Providers;

/// <summary>
/// Ortak gelen kutusunun ÜRÜN SORULARI kartı — <see cref="ChannelQuestion"/> kayıtlarından SALT OKUMA özet
/// üretir (kanal-nötr: tüm satış kanalları tek sayaçta).
///
/// <para><b>Kapsam FAIL-CLOSED (<c>ChannelQuestionAppService</c> ile AYNI gerekçe):</b> tenant sınırını ABP global
/// filtresi uygular, ama şirket filtresi <c>CurrentCompanyId</c> null iken PERMISSIVE'dir — working company
/// olmayan bir bağlamda (HTTP yüzeyi/Swagger, arka plan işi) sayaç tenant'ın TÜM şirketlerinin sorularını
/// toplardı. Şirket bağlamı yoksa kart HİÇ üretilmez ve sorgu ayrıca <c>CompanyId</c> ile açıkça daraltılır.</para>
///
/// <para><b>Bekleyen</b> = kanalda hâlâ AÇIK (<see cref="ChannelQuestionStatus.Pending"/>) ve cevabı pazaryerine
/// GİTMEMİŞ (<c>AnswerState != Sent</c>). <c>== None</c> DEĞİL: taslak/kuyruktaki/başarısız satırlar hâlâ iş
/// bekler — sayaçtan düşerlerse operatör onları bir daha görmez. Bugün hiçbir satır <c>Sent</c> olmadığından
/// (push katmanı henüz yok) koşulun ikinci bacağı push açılınca anlam kazanır.</para>
/// </summary>
[ExposeServices(typeof(IInboxSummaryProvider))]
public class ChannelQuestionInboxSummaryProvider : IInboxSummaryProvider, ITransientDependency
{
    /// <summary>Ürün Soruları tam ekranının rotası — <c>ChannelQuestionListPage.razor</c> <c>@page</c>'inden okundu.</summary>
    private const string ChannelQuestionsRoute = "/channel-questions";

    /// <summary><c>TradeXpressIcons.Comments</c> sabitinin değeri (Ürün Soruları menüsünde kullanılan ikonun aynısı).
    /// <para>Sabit <c>Blazor.Client</c>'ta yaşar ve Application katmanı UI'ya referans VEREMEZ (katman yönü
    /// UI→Application) → değer burada birebir tekrarlanır. Değişirse iki yer birlikte güncellenir.</para></summary>
    private const string ChannelQuestionIconCssClass = "custom-icon-comments";

    /// <summary>Kart satırında soru metninin gösterilecek azami uzunluğu — kart bir ÖNİZLEMEDİR, tam metin
    /// tam ekranda okunur. Soru gövdesi 4000 karaktere kadar olabilir (<see cref="ChannelQuestionConsts"/>).</summary>
    private const int QuestionPreviewMaxLength = 120;

    /// <summary>Kırpma işareti (tek karakterlik yatay elips).</summary>
    private const string PreviewEllipsis = "…";

    /// <summary>"Cevap bekleyen" ölçütü — TEK KAYNAK. Aynı ifade hem SQL sayımında (<see cref="PendingExpression"/>)
    /// hem son satırların bayrağında (<see cref="IsPending"/>) kullanılır; iki kopya sapamaz.</summary>
    private static readonly Expression<Func<ChannelQuestion, bool>> PendingExpression =
        question => question.NeutralStatus == ChannelQuestionStatus.Pending
                    && question.AnswerState != ChannelAnswerState.Sent;

    private static readonly Func<ChannelQuestion, bool> IsPending = PendingExpression.Compile();

    private readonly IRepository<ChannelQuestion, Guid> _questionRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IStringLocalizer<TradeXpressResource> _localizer;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ChannelQuestionInboxSummaryProvider(
        IRepository<ChannelQuestion, Guid> questionRepository,
        ICurrentCompany currentCompany,
        IPermissionChecker permissionChecker,
        IStringLocalizer<TradeXpressResource> localizer,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _questionRepository = questionRepository;
        _currentCompany     = currentCompany;
        _permissionChecker  = permissionChecker;
        _localizer          = localizer;
        _asyncExecuter      = asyncExecuter;
    }

    public string SourceKey
    {
        get
        {
            return InboxSourceKey.ChannelQuestions;
        }
    }

    public int Order
    {
        get
        {
            return 2;
        }
    }

    /// <summary>Ürün Soruları kartını kurar. <b>null</b> = kart gösterilmez: izin ya da şirket bağlamı yoksa
    /// kullanıcının bu türde görebileceği HİÇBİR kayıt yoktur.</summary>
    public async Task<InboxCardDto?> BuildCardAsync(int recentCount)
    {
        if (!await _permissionChecker.IsGrantedAsync(TradeXpressPermissions.ChannelQuestions.Default))
        {
            return null;
        }

        if (_currentCompany.Id is not { } companyId)
        {
            return null;
        }

        var query = (await _questionRepository.GetQueryableAsync())
            .Where(question => question.CompanyId == companyId);

        var totalCount   = await _asyncExecuter.CountAsync(query);
        var pendingCount = await _asyncExecuter.CountAsync(query.Where(PendingExpression));
        var recentItems  = await BuildRecentItemsAsync(query, recentCount);

        return new InboxCardDto
        {
            SourceKey    = InboxSourceKey.ChannelQuestions,
            Title        = _localizer["ChannelQuestion:Title"],
            IconCssClass = ChannelQuestionIconCssClass,
            PendingCount = pendingCount,
            TotalCount   = totalCount,
            TargetUrl    = ChannelQuestionsRoute,
            RecentItems  = recentItems,
        };
    }

    /// <summary>Kartın önizleme satırları: EN SON GÖRÜLEN sorular (<c>FirstSeenAt</c> azalan; eşitlikte Id ile
    /// kararlı kırılır).
    /// <para><b>DİKKAT — liste ekranıyla TERS sıra, bilerek:</b> tam ekran varsayılanı en ESKİ bekleyeni üste alır
    /// (SLA: en uzun bekleyen en riskli satırdır). Kart ise "ne oldu?" sorusunu cevaplayan bir ÖZETTİR; oraya
    /// haftalık eski bir satır düşerse pano ölü görünür. Aciliyet sayaçta (<c>PendingCount</c>) taşınır,
    /// önceliklendirme tam ekranda yapılır. Sıralama <c>RemoteQuestionDate</c> üzerinden DEĞİL: N11 soru tarihi
    /// GÜN hassasiyetindedir.</para></summary>
    private async Task<List<InboxCardItemDto>> BuildRecentItemsAsync(IQueryable<ChannelQuestion> query, int recentCount)
    {
        if (recentCount <= 0)
        {
            return new List<InboxCardItemDto>();
        }

        var recent = await _asyncExecuter.ToListAsync(
            query
                .OrderByDescending(question => question.FirstSeenAt)
                .ThenByDescending(question => question.Id)
                .Take(recentCount));

        return recent
            .Select(question => new InboxCardItemDto
            {
                Id            = question.Id,
                PrimaryText   = BuildQuestionPreview(question),
                SecondaryText = question.ProductTitle,
                // Damga UTC saklanır; yerel saate çeviri UI'nın işi (kayıt=UTC / görüntü=yerel kuralı).
                OccurredAt    = question.FirstSeenAt,
                IsPending     = IsPending(question),
            })
            .ToList();
    }

    /// <summary>Soru önizlemesi: gövde yoksa başlığa düşer (pazaryerinden gövdesiz satır gelebilir — kırpma da
    /// başlığa düşme de fail-fast DEĞİL, satır görünür kalmalı).</summary>
    private static string BuildQuestionPreview(ChannelQuestion question)
    {
        var text = string.IsNullOrWhiteSpace(question.QuestionText)
            ? question.Subject
            : question.QuestionText;

        return Shorten(text, QuestionPreviewMaxLength);
    }

    /// <summary>Tek satıra indirir + kırpar: satır sonları kartın yüksekliğini bozar, uzun metin taşar.</summary>
    private static string Shorten(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var singleLine = text
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return singleLine.Substring(0, maxLength).TrimEnd() + PreviewEllipsis;
    }
}
