using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mvc.Data;
using mvc.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using ClosedXML.Excel;

namespace mvc.Controllers
{
    public class BookController : Controller
    {
        private readonly IBookRepository _repository;

        public BookController(IBookRepository repository)
        {
            _repository = repository;
        }

        // GET: Book
        [Authorize(Policy = "MemberOnly")]
        public IActionResult Index()
        {
            return View(_repository.GetAll());
        }

        // GET: Book/Details/5
        [Authorize(Policy = "MemberOnly")]
        public IActionResult Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            BookViewModel? book = _repository.GetById(id);
            if (book == null)
            {
                return NotFound();
            }

            return View(book);
        }

        // GET: Book/Create
        [Authorize(Policy = "MemberOnly")]
        public IActionResult Create()
        {
            return View(new BookViewModel { Title = "", Author = "" });
        }

        // POST: Book/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [Authorize(Policy = "MemberOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult Create([Bind("Id,Title,Author,Price")] BookViewModel bookViewModel)
        {
            if (ModelState.IsValid)
            {
                _repository.Add(bookViewModel);
                HttpContext.Items["AuditDetails"] = $"Created book '{bookViewModel.Title}' (price {bookViewModel.Price}).";
                return RedirectToAction(nameof(Index));
            }
            return View(bookViewModel);
        }

        // GET: Book/Edit/5
        [Authorize(Policy = "MemberOnly")]
        public IActionResult Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            BookViewModel? book = _repository.GetById(id);
            if (book == null)
            {
                return NotFound();
            }

            return View(book);
        }

        // POST: Book/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [Authorize(Policy = "MemberOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, [Bind("Id,Title,Author,Price")] BookViewModel bookViewModel)
        {
            if (ModelState.IsValid)
            {
                _repository.Update(id, bookViewModel);
                HttpContext.Items["AuditDetails"] = $"Updated book id {id} ('{bookViewModel.Title}').";
                return RedirectToAction(nameof(Index));
            }
            return View(bookViewModel);
        }

        // GET: Book/Delete/5
        [Authorize(Policy = "MemberOnly")]
        public IActionResult Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            BookViewModel? book = _repository.GetById(id);
            if (book == null)
            {
                return NotFound();
            }

            return View(book);
        }

        // POST: Book/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Policy = "MemberOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            _repository.Delete(id);
            HttpContext.Items["AuditDetails"] = $"Deleted book id {id}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Book/Search
        [HttpPost, ActionName("Search")]
        [Authorize(Policy = "MemberOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult Search(string searchString)
        {
            List<BookViewModel> searchResults = _repository.Search(searchString);
            return View(searchResults);
        }

        //Book/PdfReport
        [Authorize(Policy = "MemberOnly")]
        public IActionResult PdfReport()
        {
            List<BookViewModel> books = _repository.GetAll();

            //Create PDF
            using (MemoryStream ms = new MemoryStream())
            {
                iTextSharp.text.Document doc = new iTextSharp.text.Document(PageSize.A4);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();
                doc.Add(new Paragraph("Book Report"));
                doc.Add(new Paragraph("Generated on:" + DateTime.Now.ToString()));
                doc.Add(new Paragraph(" "));
                PdfPTable table = new PdfPTable(4);
                table.AddCell("Id");
                table.AddCell("Title");
                table.AddCell("Author");
                table.AddCell("Price");

                foreach (var book in books)
                {
                    table.AddCell(book.Id.ToString());
                    table.AddCell(book.Title);
                    table.AddCell(book.Author);
                    table.AddCell(book.Price.ToString());
                }
                doc.Add(table);
                doc.Close();
                return File(ms.ToArray(), "application/pdf", "PdfReport" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".pdf");
            }
        }

        //Book/ExcelReport
        [Authorize(Policy = "MemberOnly")]
        public IActionResult ExcelReport()
        {
            List<BookViewModel> books = _repository.GetAll();

            //Create Excel
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Books");
                worksheet.Cell(1, 1).Value = "Id";
                worksheet.Cell(1, 2).Value = "Title";
                worksheet.Cell(1, 3).Value = "Author";
                worksheet.Cell(1, 4).Value = "Price";
                for (int i = 0; i < books.Count; i++)
                {
                    worksheet.Cell(i + 2, 1).Value = books[i].Id;
                    worksheet.Cell(i + 2, 2).Value = books[i].Title;
                    worksheet.Cell(i + 2, 3).Value = books[i].Author;
                    worksheet.Cell(i + 2, 4).Value = books[i].Price;
                }
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ExcelReport" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx");
                }
            }
        }
    }
}
