using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.ObjectMapping;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// <see cref="ICommitCoordinator{TGetDto,TListDto,TKey,TListRequestDto}"/>'nun PERSISTENT uygulaması —
/// üst-seviye liste bağlamı. <b>TEK API sınırı:</b> liste-fetch + commit (GetDto→Create/Update map) + get +
/// sil hepsi AppService'e gider. Host (liste sayfası / EditHost) bunu kendi concrete AppService'iyle kurar
/// (kapalı generic DI'dan çözülmediği için); agnostic <c>EntityEditForm</c> event'le buraya delege eder.
/// Nav v1'de minimal (sayfa-aşırı gezinme rollout'ta StateService köprüsüyle birleştirilecek).
/// </summary>
public class PersistentCoordinator<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto>
    : ICommitCoordinator<TGetDto, TListDto, TKey, TListRequestDto>
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : class, new()
    where TCreateDto : class, new()
    where TUpdateDto : class, new()
{
    private readonly ICrudAppService<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> _appService;
    private readonly IObjectMapper _mapper;

    public PersistentCoordinator(
        ICrudAppService<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> appService,
        IObjectMapper mapper)
    {
        _appService = appService;
        _mapper = mapper;
    }

    public TGetDto NewModel() => new();

    public Task<TGetDto> GetForEditAsync(TKey id) => _appService.GetAsync(id);

    public async Task<TGetDto> CommitAsync(TGetDto model)
    {
        // Id boş → Create, dolu → Update. GetDto→Create/Update map'i BURADA (Form bilmez).
        if (IsEmptyKey(model.Id))
        {
            var createDto = _mapper.Map<TGetDto, TCreateDto>(model);
            return await _appService.CreateAsync(createDto);
        }

        var updateDto = _mapper.Map<TGetDto, TUpdateDto>(model);
        return await _appService.UpdateAsync(model.Id, updateDto);
    }

    public Task DeleteAsync(TKey id) => _appService.DeleteAsync(id);

    public Task<PagedResultDto<TListDto>> FetchAsync(TListRequestDto request) => _appService.GetListAsync(request);

    // Nav v1'de minimal.
    public bool CanGoPrevious => false;
    public bool CanGoNext => false;
    public Task<TGetDto?> GoPreviousAsync() => Task.FromResult<TGetDto?>(null);
    public Task<TGetDto?> GoNextAsync() => Task.FromResult<TGetDto?>(null);

    private static bool IsEmptyKey(TKey key) => EqualityComparer<TKey>.Default.Equals(key, default!);
}
