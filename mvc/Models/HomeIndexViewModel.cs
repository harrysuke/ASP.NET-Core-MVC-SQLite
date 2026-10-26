namespace mvc.Models
{
    public class HomeIndexViewModel
    {
        public int TotalBooks { get; set; }
        public int TotalValue { get; set; }
        public double AvgPrice { get; set; }
        public List<BookViewModel> FeaturedBooks { get; set; } = new();
    }
}