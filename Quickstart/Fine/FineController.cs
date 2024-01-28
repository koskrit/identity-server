using Microsoft.AspNetCore.Mvc;

namespace YourNamespace.Controllers
{
    public class FineController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
