using System.Threading.Tasks;

namespace Integration.TradeXpress.Data;

public interface ITradeXpressDbSchemaMigrator
{
    Task MigrateAsync();
}
