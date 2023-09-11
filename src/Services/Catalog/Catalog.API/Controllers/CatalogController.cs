using Azure;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.TextEmbedding;
using Microsoft.SemanticKernel.Connectors.Memory.Sqlite;
using Microsoft.SemanticKernel.Memory;

namespace Microsoft.eShopOnContainers.Services.Catalog.API.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class CatalogController : ControllerBase
{
    private readonly CatalogContext _catalogContext;
    private readonly CatalogSettings _settings;
    private readonly ICatalogIntegrationEventService _catalogIntegrationEventService;

    private const string AoaiKey = "TODO GET KEY FROM KEY VAULT";
    private const string AoaiEndpoint = "TODO GET ENDPOINT FROM CONFIG";

    public CatalogController(CatalogContext context, IOptionsSnapshot<CatalogSettings> settings, ICatalogIntegrationEventService catalogIntegrationEventService)
    {
        _catalogContext = context ?? throw new ArgumentNullException(nameof(context));
        _catalogIntegrationEventService = catalogIntegrationEventService ?? throw new ArgumentNullException(nameof(catalogIntegrationEventService));
        _settings = settings.Value;

        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    // GET api/v1/[controller]/items[?pageSize=3&pageIndex=10]
    [HttpGet]
    [Route("items")]
    [ProducesResponseType(typeof(PaginatedItemsViewModel<CatalogItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IEnumerable<CatalogItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ItemsAsync([FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0, string ids = null)
    {
        if (!string.IsNullOrEmpty(ids))
        {
            var items = await GetItemsByIdsAsync(ids);

            if (!items.Any())
            {
                return BadRequest("ids value invalid. Must be comma-separated list of numbers");
            }

            return Ok(items);
        }

        var totalItems = await _catalogContext.CatalogItems
            .LongCountAsync();

        var itemsOnPage = await _catalogContext.CatalogItems
            .OrderBy(c => c.Name)
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        itemsOnPage = ChangeUriPlaceholder(itemsOnPage);

        var model = new PaginatedItemsViewModel<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);

        return Ok(model);
    }

    private async Task<List<CatalogItem>> GetItemsByIdsAsync(string ids)
    {
        var numIds = ids.Split(',').Select(id => (Ok: int.TryParse(id, out int x), Value: x));

        if (!numIds.All(nid => nid.Ok))
        {
            return new List<CatalogItem>();
        }

        var idsToSelect = numIds
            .Select(id => id.Value);

        var items = await _catalogContext.CatalogItems.Where(ci => idsToSelect.Contains(ci.Id)).ToListAsync();

        items = ChangeUriPlaceholder(items);

        return items;
    }

    [HttpGet]
    [Route("items/{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogItem>> ItemByIdAsync(int id)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var item = await _catalogContext.CatalogItems.SingleOrDefaultAsync(ci => ci.Id == id);

        var baseUri = _settings.PicBaseUrl;
        var azureStorageEnabled = _settings.AzureStorageEnabled;

        item.FillProductUrl(baseUri, azureStorageEnabled: azureStorageEnabled);

        if (item != null)
        {
            return item;
        }

        return NotFound();
    }

    // GET api/v1/[controller]/items/withname/samplename[?pageSize=3&pageIndex=10]
    [HttpGet]
    [Route("items/withname/{name:minlength(1)}")]
    public async Task<ActionResult<PaginatedItemsViewModel<CatalogItem>>> ItemsWithNameAsync(string name, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0)
    {
        var totalItems = await _catalogContext.CatalogItems
            .Where(c => c.Name.StartsWith(name))
            .LongCountAsync();

        var itemsOnPage = await _catalogContext.CatalogItems
            .Where(c => c.Name.StartsWith(name))
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        itemsOnPage = ChangeUriPlaceholder(itemsOnPage);

        return new PaginatedItemsViewModel<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    // GET api/v1/[controller]/items/withsemantic/text[?pageSize=3&pageIndex=10]
    [HttpGet]
    [Route("items/withsemantic/{text:minlength(1)}")]
    public async Task<ActionResult<PaginatedItemsViewModel<CatalogItem>>> ItemsWithSemanticAsync(string text, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0)
    {
        await EnsureMemoryStore();

        var client = new OpenAIClient(new Uri(AoaiEndpoint), new AzureKeyCredential(AoaiKey));

        // TODO: This is a hack for demo purposes and is just a placeholder until the catalog db is
        // replaced by one in which we can include embedding vectors as part of each catalog entry.
        // At that point, the query we issue will include the embedding vector for the text we are
        // searching for, and we'll ask the db to include similarity as part of its query. For now for
        // demo purposes, this is just fetching the whole catalog and filtering/sorting it ourselves.

        var idsAndScores = new Dictionary<int, double>();
        await foreach (var result in s_semanticTextMemory.SearchAsync(nameof(Catalog), text, limit: int.MaxValue, minRelevanceScore: 0.78))
        {
            if (int.TryParse(result.Metadata.Id, CultureInfo.InvariantCulture, out int id))
            {
                idsAndScores.TryAdd(id, result.Relevance);
            }
        }

        var items = _catalogContext.CatalogItems.ToList();
        items.RemoveAll(item => !idsAndScores.ContainsKey(item.Id));
        items.Sort((x, y) => idsAndScores[y.Id].CompareTo(idsAndScores[x.Id]));

        var totalItems = items.LongCount();

        var itemsOnPage = items
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToList();

        itemsOnPage = ChangeUriPlaceholder(itemsOnPage);

        return new PaginatedItemsViewModel<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    // GET api/v1/[controller]/items/type/1/brand[?pageSize=3&pageIndex=10]
    [HttpGet]
    [Route("items/type/{catalogTypeId}/brand/{catalogBrandId:int?}")]
    public async Task<ActionResult<PaginatedItemsViewModel<CatalogItem>>> ItemsByTypeIdAndBrandIdAsync(int catalogTypeId, int? catalogBrandId, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0)
    {
        var root = (IQueryable<CatalogItem>)_catalogContext.CatalogItems;

        root = root.Where(ci => ci.CatalogTypeId == catalogTypeId);

        if (catalogBrandId.HasValue)
        {
            root = root.Where(ci => ci.CatalogBrandId == catalogBrandId);
        }

        var totalItems = await root
            .LongCountAsync();

        var itemsOnPage = await root
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        itemsOnPage = ChangeUriPlaceholder(itemsOnPage);

        return new PaginatedItemsViewModel<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    // GET api/v1/[controller]/items/type/all/brand[?pageSize=3&pageIndex=10]
    [HttpGet]
    [Route("items/type/all/brand/{catalogBrandId:int?}")]
    public async Task<ActionResult<PaginatedItemsViewModel<CatalogItem>>> ItemsByBrandIdAsync(int? catalogBrandId, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0)
    {
        var root = (IQueryable<CatalogItem>)_catalogContext.CatalogItems;

        if (catalogBrandId.HasValue)
        {
            root = root.Where(ci => ci.CatalogBrandId == catalogBrandId);
        }

        var totalItems = await root
            .LongCountAsync();

        var itemsOnPage = await root
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        itemsOnPage = ChangeUriPlaceholder(itemsOnPage);

        return new PaginatedItemsViewModel<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    // GET api/v1/[controller]/CatalogTypes
    [HttpGet]
    [Route("catalogtypes")]
    public async Task<ActionResult<List<CatalogType>>> CatalogTypesAsync()
    {
        return await _catalogContext.CatalogTypes.ToListAsync();
    }

    // GET api/v1/[controller]/CatalogBrands
    [HttpGet]
    [Route("catalogbrands")]
    public async Task<ActionResult<List<CatalogBrand>>> CatalogBrandsAsync()
    {
        return await _catalogContext.CatalogBrands.ToListAsync();
    }

    //PUT api/v1/[controller]/items
    [Route("items")]
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult> UpdateProductAsync([FromBody] CatalogItem productToUpdate)
    {
        var catalogItem = await _catalogContext.CatalogItems.SingleOrDefaultAsync(i => i.Id == productToUpdate.Id);

        if (catalogItem == null)
        {
            return NotFound(new { Message = $"Item with id {productToUpdate.Id} not found." });
        }

        var oldPrice = catalogItem.Price;
        var raiseProductPriceChangedEvent = oldPrice != productToUpdate.Price;

        // Update current product
        catalogItem = productToUpdate;
        _catalogContext.CatalogItems.Update(catalogItem);

        if (raiseProductPriceChangedEvent) // Save product's data and publish integration event through the Event Bus if price has changed
        {
            //Create Integration Event to be published through the Event Bus
            var priceChangedEvent = new ProductPriceChangedIntegrationEvent(catalogItem.Id, productToUpdate.Price, oldPrice);

            // Achieving atomicity between original Catalog database operation and the IntegrationEventLog thanks to a local transaction
            await _catalogIntegrationEventService.SaveEventAndCatalogContextChangesAsync(priceChangedEvent);

            // Publish through the Event Bus and mark the saved event as published
            await _catalogIntegrationEventService.PublishThroughEventBusAsync(priceChangedEvent);
        }
        else // Just save the updated product because the Product's Price hasn't changed.
        {
            await _catalogContext.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(ItemByIdAsync), new { id = productToUpdate.Id }, null);
    }

    //POST api/v1/[controller]/items
    [Route("items")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateProductAsync([FromBody] CatalogItem product)
    {
        var item = new CatalogItem
        {
            CatalogBrandId = product.CatalogBrandId,
            CatalogTypeId = product.CatalogTypeId,
            Description = product.Description,
            Name = product.Name,
            PictureFileName = product.PictureFileName,
            Price = product.Price
        };

        _catalogContext.CatalogItems.Add(item);

        await _catalogContext.SaveChangesAsync();

        // TODO: Once we have embedding vectors in the catalog db, this will be replaced with
        // updating item with its embedding vector prior to saving it.
        await EnsureMemoryStore();
        await SaveItemReference(item);

        return CreatedAtAction(nameof(ItemByIdAsync), new { id = item.Id }, null);
    }

    //DELETE api/v1/[controller]/id
    [Route("{id}")]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProductAsync(int id)
    {
        var product = _catalogContext.CatalogItems.SingleOrDefault(x => x.Id == id);

        if (product == null)
        {
            return NotFound();
        }

        _catalogContext.CatalogItems.Remove(product);

        await _catalogContext.SaveChangesAsync();

        return NoContent();
    }

    private List<CatalogItem> ChangeUriPlaceholder(List<CatalogItem> items)
    {
        var baseUri = _settings.PicBaseUrl;
        var azureStorageEnabled = _settings.AzureStorageEnabled;

        foreach (var item in items)
        {
            item.FillProductUrl(baseUri, azureStorageEnabled: azureStorageEnabled);
        }

        return items;
    }

    // TODO: This is all temporary and will go away once we have a real catalog db that includes embedding vectors
    private static readonly SemaphoreSlim s_lock = new SemaphoreSlim(1, 1);
    private static ISemanticTextMemory s_semanticTextMemory;
    private static ITextEmbeddingGeneration s_embeddingGeneration;

    private static Task SaveItemReference(CatalogItem item) =>
        s_semanticTextMemory.SaveReferenceAsync(nameof(Catalog), $"""{item.Name} {item.Description}""", item.Id.ToString(CultureInfo.InvariantCulture), nameof(Catalog), item.Description);

    private async Task EnsureMemoryStore()
    {
        if (s_semanticTextMemory is null)
        {
            await s_lock.WaitAsync();
            try
            {
                if (s_semanticTextMemory is null)
                {
                    var memoryStore = await SqliteMemoryStore.ConnectAsync("catalog.sqlitedb");
                    s_embeddingGeneration = new AzureTextEmbeddingGeneration(
                            "TextEmbeddingAda002_1",
                            AoaiEndpoint,
                            AoaiKey);
                    s_semanticTextMemory = new SemanticTextMemory(memoryStore, s_embeddingGeneration);

                    IList<string> collections = await s_semanticTextMemory.GetCollectionsAsync();
                    if (!collections.Contains(nameof(Catalog)))
                    {
                        await foreach (var item in _catalogContext.CatalogItems)
                        {
                            await SaveItemReference(item);
                        }
                    }
                }
            }
            finally
            {
                s_lock.Release();
            }
        }
    }
}
