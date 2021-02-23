using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RestAPIClient.Pages
{
    public class IndexModel : PageModel
    {
        [BindProperty]
        public string Deep { get; set; }

        public void OnGet()
        {

        }

        public async Task<IActionResult> OnPost()
        {
            string responseContent = "[]";
            try
            {
                var stringURL = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/URLCaller/depth/{Deep}";
                Uri baseURL = new Uri(stringURL);

                HttpClient client = new HttpClient();

                HttpResponseMessage response = await client.GetAsync(baseURL.ToString());

                if (response.IsSuccessStatusCode)
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                }

                return RedirectToPage("Response", new { result = responseContent });
            }
            catch (ArgumentNullException uex)
            {
                return RedirectToPage("Error", new { msg = uex.Message + " | URL missing or invalid." });
            }
            catch (JsonReaderException jex)
            {
                return RedirectToPage("Error", new { msg = jex.Message + " | Json data could not be read." });
            }
            catch (Exception ex)
            {
                return RedirectToPage("Error", new { msg = ex.Message + " | Are you missing some Json keys and values? Please check your Json data." });
            }


        }
    }
}
