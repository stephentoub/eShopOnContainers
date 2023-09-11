namespace Microsoft.eShopOnContainers.WebMVC.Controllers;

public class CatalogController : Controller
{
    private ICatalogService _catalogSvc;

    public CatalogController(ICatalogService catalogSvc) =>
        _catalogSvc = catalogSvc;

    public async Task<IActionResult> Index(string search, int? page, [FromQuery] string errorMsg)
    {
        var itemsPage = 9;
        var catalog = await _catalogSvc.GetCatalogItems(page ?? 0, itemsPage, search);
        var vm = new IndexViewModel()
        {
            CatalogItems = catalog.Data,
            Brands = await _catalogSvc.GetBrands(),
            Types = await _catalogSvc.GetTypes(),
            SearchText = search,
            PaginationInfo = new PaginationInfo()
            {
                ActualPage = page ?? 0,
                ItemsPerPage = catalog.Data.Count,
                TotalItems = catalog.Count,
                TotalPages = (int)Math.Ceiling(((decimal)catalog.Count / itemsPage))
            }
        };

        vm.PaginationInfo.Next = (vm.PaginationInfo.ActualPage == vm.PaginationInfo.TotalPages - 1) ? "is-disabled" : "";
        vm.PaginationInfo.Previous = (vm.PaginationInfo.ActualPage == 0) ? "is-disabled" : "";

        ViewBag.BasketInoperativeMsg = errorMsg;

        return View(vm);
    }
}
