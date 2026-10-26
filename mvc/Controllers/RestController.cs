using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using mvc.Data;
using mvc.Models;

namespace mvc.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "MemberOnly")]
    public class RestController : ControllerBase
    {
        private readonly IBookRepository _repository;

        public RestController(IBookRepository repository)
        {
            _repository = repository;
        }

        // READ
        [HttpGet]
        public IActionResult GetBooks()
        {
            return Ok(_repository.GetAll());
        }

        // CREATE
        [HttpPost]
        public IActionResult AddBook([FromBody] BookViewModel book)
        {
            _repository.Add(book);
            HttpContext.Items["AuditDetails"] = $"API: created book '{book.Title}'.";
            return Ok("Record added successfully");
        }

        // UPDATE
        [HttpPut("{id}")]
        public IActionResult UpdateBook(int id, [FromBody] BookViewModel book)
        {
            _repository.Update(id, book);
            HttpContext.Items["AuditDetails"] = $"API: updated book id {id}.";
            return Ok("Record updated successfully");
        }

        // DELETE
        [HttpDelete("{id}")]
        public IActionResult DeleteBook(int id)
        {
            _repository.Delete(id);
            HttpContext.Items["AuditDetails"] = $"API: deleted book id {id}.";
            return Ok("Record deleted successfully");
        }
    }
}