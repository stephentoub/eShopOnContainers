namespace WebMVC.Controllers;

public class ConciergeController : Controller
{
    public IActionResult Index()
    {
        return View("Host");
    }
}
