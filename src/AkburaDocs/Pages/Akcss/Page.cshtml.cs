using Microsoft.AspNetCore.Mvc;

namespace AkburaDocs.Pages.Akcss;

public class PageModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    [FromRoute]
    public string Slug { get; set; }
}
