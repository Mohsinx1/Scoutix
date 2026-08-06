using Microsoft.EntityFrameworkCore;
using Scoutix.Enrichment.Data;
using Scoutix.Enrichment.Model;

namespace Scoutix.Tests;

public class CandidateStoreTests
{
    private static EnrichmentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<EnrichmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Upsert_SameSourceTwice_KeepsOneRow()
    {
        using var ctx = NewContext();
        var store = new CandidateStore(ctx);

        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane Smith", EnrichmentSources.Website, 0.6);
        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane Smith", EnrichmentSources.Website, 0.6);

        var rows = await store.GetForListingAsync(1);
        Assert.Single(rows);
    }

    [Fact]
    public async Task Upsert_SameSourceNewValue_UpdatesInPlace()
    {
        using var ctx = NewContext();
        var store = new CandidateStore(ctx);

        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane", EnrichmentSources.Npi, 0.5, "npi:111");
        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane A Smith", EnrichmentSources.Npi, 0.9, "npi:111");

        var rows = await store.GetForListingAsync(1);
        Assert.Single(rows);
        Assert.Equal("Jane A Smith", rows[0].Value);
        Assert.Equal(0.9, rows[0].Confidence);
    }

    [Fact]
    public async Task Upsert_DifferentSources_CreateSeparateRows()
    {
        using var ctx = NewContext();
        var store = new CandidateStore(ctx);

        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane Smith", EnrichmentSources.Website, 0.6);
        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane Smith", EnrichmentSources.Npi, 0.9);

        var rows = await store.GetForListingAsync(1);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Upsert_DifferentFields_CreateSeparateRows()
    {
        using var ctx = NewContext();
        var store = new CandidateStore(ctx);

        await store.UpsertAsync(1, EnrichmentField.OwnerName, "Jane Smith", EnrichmentSources.Website, 0.6);
        await store.UpsertAsync(1, EnrichmentField.Email, "jane@acmedental.com", EnrichmentSources.Website, 0.6);

        var rows = await store.GetForListingAsync(1);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Upsert_BlankValue_Throws()
    {
        using var ctx = NewContext();
        var store = new CandidateStore(ctx);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(1, EnrichmentField.OwnerName, "   ", EnrichmentSources.Website, 0.6));
    }
}
