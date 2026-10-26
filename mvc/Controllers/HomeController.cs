using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mvc.Data;
using mvc.Models;
using System.Diagnostics;

namespace mvc.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IBookRepository _bookRepository;

        public HomeController(ILogger<HomeController> logger, IBookRepository bookRepository)
        {
            _logger = logger;
            _bookRepository = bookRepository;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            HomeIndexViewModel model = new HomeIndexViewModel();

            if (User.Identity?.IsAuthenticated == true)
            {
                List<BookViewModel> books = _bookRepository.GetAll();
                model.TotalBooks = books.Count;
                model.TotalValue = books.Sum(b => b.Price);
                model.AvgPrice = books.Count > 0 ? Math.Round(books.Average(b => b.Price), 2) : 0;
                model.FeaturedBooks = books.OrderByDescending(b => b.Id).Take(6).ToList();
            }

            return View(model);
        }

        [AllowAnonymous]
        public IActionResult Api()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
