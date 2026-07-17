BEGIN TRANSACTION;
ALTER TABLE [AppProductVariantRecipeLines] ADD [CommodityVariantId] uniqueidentifier NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260717022013_AddCommodityVariantIdToProductRecipeLine', N'10.0.9');

COMMIT;
GO

