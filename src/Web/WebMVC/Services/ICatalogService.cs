namespace Microsoft.eShopOnContainers.WebMVC.Services;

public interface ICatalogService
{
    Task<Catalog> GetCatalogItems(int page, int take, string search);
    Task<IEnumerable<SelectListItem>> GetBrands();
    Task<IEnumerable<SelectListItem>> GetTypes();
}
