namespace Microsoft.eShopOnContainers.WebMVC.ViewModels.CatalogViewModels;

public class IndexViewModel
{
    public IEnumerable<CatalogItem> CatalogItems { get; set; }
    public IEnumerable<SelectListItem> Brands { get; set; }
    public IEnumerable<SelectListItem> Types { get; set; }
    public string SearchText { get; set; }
    public PaginationInfo PaginationInfo { get; set; }
}
