using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Collections.Specialized;
using HtmlAgilityPack;
using PuppeteerSharp;
using jobscrapper;

namespace JobvisionScraper
{
    public class Program
    {
        private static IBrowser? browser;
        private static readonly Dictionary<string, string> KnownSlugs = new()
        {
            { "توسعه نرم افزار و برنامه نویسی", "developer" },
            { "تست نرم افزار", "software-testing" },
            { "شبکه / DevOps / پشتیبانی سخت افزاری و نرم افزاری", "network-devops" },
            { "علوم داده / هوش مصنوعی", "data-science" },
            { "طراحی بازی", "game-design" },
            { "طراحی گرافیک / طراحی انیمیشن و موشن گرافیک", "graphic-design" },
            { "دیجیتال مارکتینگ و سئو", "digital-marketing" },
            { "مالی و حسابداری", "accounting" },
            { "مهندسی مکانیک / مهندسی هوا و فضا", "mechanical-engineering" },
            { "آموزش / تدریس", "education" },
            { "مسئول دفتر / کارمند اداری و ثبت اطلاعات / تایپیست", "administrative" },
            { "معامله گر و تحلیل گر بازارهای مالی", "financial-trader" },
            { "کفاش", "shoemaker" },
            { "مهندسی شیمی / مهندسی نفت و گاز", "chemical-petroleum" },
            { "طراحی رابط و تجربه کاربری (UI/UX)", "ui-ux" },
            { "فروش و بازاریابی - سطوح کارشناسی و مدیریتی", "sales-marketing-managerial" },
        };

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Starting Jobvision.ir scraper with Puppeteer...");
            try
            {
                Console.WriteLine("Downloading Chromium if needed...");
                await new BrowserFetcher().DownloadAsync();
                Console.WriteLine("Launching browser...");
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true, // Set true for production
                    HeadlessMode = HeadlessMode.True,
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
                });
                Console.WriteLine("Browser launched successfully.");
                var categories = await GetCategories();
                Console.WriteLine($"Found {categories.Count} categories.");
                if (categories.Count > 0)
                {
                    Console.WriteLine($"Sample category: {categories[0].Name} ({categories[0].Slug})");
                }
                bool fetchFullDesc = true; // Set false for speed (skips desc fetch)
                int catIndex = 0;
                foreach (var category in categories) // Sequential categories like original
                {
                    catIndex++;
                    Console.WriteLine($"\n--- Starting category {catIndex} of {categories.Count}: {category.Name} ({category.Slug}) ---");
                    var allJobs = new List<Job>();
                    var baseUrl = $"https://jobvision.ir/jobs/category/{category.Slug}";
                    var currentUrl = baseUrl;
                    var pageNum = 1;
                    var maxPages = 5; // Adjust (5 = ~150 jobs/cat; full site 100+)
                    while (!string.IsNullOrEmpty(currentUrl) && pageNum <= maxPages)
                    {
                        Console.WriteLine($"Page {pageNum}: {currentUrl}");
                        var pageJobs = await ScrapeJobsFromUrl(currentUrl, category.Name, "job-card", fetchFullDesc);
                        allJobs.AddRange(pageJobs);
                        currentUrl = GetNextPageUrl(currentUrl);
                        pageNum++;
                        await Task.Delay(2000); // Reduced delay between pages for less wait
                    }
                    // Output to JSONL
                    var options = new JsonSerializerOptions { WriteIndented = false };
                    var fileName = $"jobs_{SanitizeFileName(category.Name)}.jsonl";
                    await File.WriteAllLinesAsync(fileName, allJobs.Select(job => JsonSerializer.Serialize(job, options)), Encoding.UTF8);
                    Console.WriteLine($"Saved {allJobs.Count} jobs to {fileName} (JSONL format for ML). Total so far: {allJobs.Count}");
                    await Task.Delay(5000); // Reduced delay between categories
                }
                Console.WriteLine("All categories completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\nStack: {ex.StackTrace}");
            }
            finally
            {
                await browser?.CloseAsync();
                browser?.Dispose();
                Console.WriteLine("Browser closed.");
            }
            Console.WriteLine("Full scraping complete. Check JSONL files for ML use.");
        }

        private static async Task<List<(string Name, string Slug)>> GetCategories()
        {
            var categories = new List<(string Name, string Slug)>();
            try
            {
                Console.WriteLine("Fetching categories...");
                var url = "https://jobvision.ir/jobs";
                var html = await GetRenderedHtmlAsync(url, null);
                Console.WriteLine($"Categories HTML length: {html.Length}");
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                Console.WriteLine("HTML loaded into HtmlDocument for categories.");
                var optionNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'ng-option')]//span[contains(@class, 'ng-option-label')]");
                Console.WriteLine($"Static option nodes found: {optionNodes?.Count ?? 0}");
                if (optionNodes != null && optionNodes.Count > 0)
                {
                    foreach (var node in optionNodes)
                    {
                        var name = node.InnerText.Trim();
                        if (!string.IsNullOrEmpty(name) && name != "همه مشاغل")
                        {
                            var slug = GetSlugFromName(name);
                            categories.Add((name, slug));
                        }
                    }
                    Console.WriteLine($"Extracted {categories.Count} categories from static HTML.");
                    return categories;
                }
                Console.WriteLine("Trying dynamic category extraction...");
                var page = await browser!.NewPageAsync();
                await page.GoToAsync(url, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle0 } });
                await page.WaitForSelectorAsync(".ng-dropdown-panel", new WaitForSelectorOptions { Timeout = 10000 });
                await page.ClickAsync(".ng-select-container");
                await Task.Delay(1000);
                var optionTexts = await page.EvaluateFunctionAsync<string[]>(
                    @"() => {
                        const labels = document.querySelectorAll('.ng-option .ng-option-label');
                        return Array.from(labels).map(el => el.textContent.trim()).filter(name => name && name !== 'همه مشاغل');
                    }");
                await page.CloseAsync();
                if (optionTexts != null && optionTexts.Length > 0)
                {
                    categories = optionTexts.Select(name => (name, GetSlugFromName(name))).ToList();
                    Console.WriteLine($"Extracted {categories.Count} categories dynamically.");
                    return categories;
                }
                else
                {
                    Console.WriteLine("Dynamic extraction returned empty.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dynamic category fetch failed: {ex.Message}. Using fallback.");
            }
            Console.WriteLine("Using fallback categories...");
            var fallbackNames = new[]
            {
                //"فروش و بازاریابی - سطوح کارشناسی و مدیریتی",
                //"فروش و بازاریابی - فروشنده / بازاریاب و ویزیتور / صندوقدار",
                //"مدیر فروشگاه / مدیر رستوران",
                //"خدمات و پشتیبانی مشتریان",
                //"نماینده علمی / مدرپ",
                //"مدیریت بیمه",
                //"دیجیتال مارکتینگ و سئو",
                //"ترجمه / تولید محتوا / نویسندگی و ویراستاری",
                //"توسعه نرم افزار و برنامه نویسی",
                //"تست نرم افزار",
                //"شبکه / DevOps / پشتیبانی سخت افزاری و نرم افزاری",
                //"علوم داده / هوش مصنوعی",
                //"طراحی بازی",
                //"طراحی گرافیک / طراحی انیمیشن و موشن گرافیک",
                //"طراحی لباس / طراحی طلا و جواهر",
                //"طراحی صنعتی / نقشه کشی صنعتی",
                //"عکاسی",
                //"مشاغل حوزه فیلم و سینما",
                //"طراحی موسیقی و صدا",
                //"طراحی رابط و تجربه کاربری (UI/UX)",
                //"مدیر محصول / مالک محصول",
                //"تحلیل و توسعه کسب و کار / استراتژی / برنامه ریزی",
                //"مهندسی صنایع / مدیریت تولید / مدیریت پروژه / مدیریت عملیات",
                //"خرید / تدارکات",
                //"بازرگانی / تجارت",
                //"لجستیک / حمل و نقل / انبارداری",
                //"راننده / مسئول توزیع / پیک موتوری",
                //"مالی و حسابداری",
                //"معامله گر و تحلیل گر بازارهای مالی",
                //"تحصیل دار / کارپرداز",
                //"مسئول دفتر / کارمند اداری و ثبت اطلاعات / تایپیست",
                //"منابع انسانی",
                //"مدیر اجرایی / مدیر داخلی",
                //"مدیرعامل / مدیر کارخانه",
                //"مهندسی برق",
                //"مهندسی پزشکی",
                //"مهندسی مکانیک / مهندسی هوا و فضا",
                //"مهندسی صنایع غذایی",
                //"مهندسی شیمی / مهندسی نفت و گاز",
                //"مهندسی انرژی / مهندسی هسته ای",
                //"بهداشت، ایمنی و محیط زیست (HSE)",
                //"مهندسی عمران",
                //"مهندسی معماری و شهرسازی",
                //"مهندسی معدن / زمین شناسی",
                //"مهندسی مواد و متالورژی",
                //"مهندسی نساجی",
                //"مهندسی پلیمر",
                //"مهندسی کشاورزی / علوم دامی",
                //"زیست شناسی / علوم زیستی / علوم آزمایشگاهی",
                //"داروسازی / بیوشیمی / شیمی",
                //"پزشک / دندانپزشک / دامپزشک",
                //"پرستار و بهیار / تکنسین حوزه سلامت و درمان / دستیار پزشک",
                "پرستار سالمند / پرستار کودک",
                "روانشناسی / مشاوره / علوم اجتماعی",
                "حقوقی",
                "روابط عمومی",
                "خبرنگار / روزنامه نگار",
                "آموزش / تدریس",
                "پژوهش",
                "نگهبان",
                "کارگر ساده / نیروی خدماتی",
                "تکنسین فنی / تعمیرکار / کارگر ماهر",
                "تخصص های ساختمانی (بنّا / گچ کار / کاشی کار و ...)",
                "نجار / MDF کار / کابینت کار / مبل ساز / رنگ کار چوب",
                "آرایشگر",
                "قناد و شیرینی پز",
                "بافنده فرش (قالی باف)",
                "نانوا",
                "قفل و کلیدساز",
                "قصاب",
                "کفاش",
                "خیاط",
                "آشپز",
                "باریستا / کافی من / گارسون",
                "راهنمای تور / مهماندار",
                "ورزش / تربیت بدنی / تغذیه",
                "تاریخ / جغرافیا / باستان شناسی"
            };
            return fallbackNames.Select(name => (name, GetSlugFromName(name))).ToList();
        }

        private static async Task<string> GetRenderedHtmlAsync(string url, string? selector = null, int retry = 3)
        {
            if (browser == null) throw new InvalidOperationException("Browser not initialized.");
            for (int i = 0; i < retry; i++)
            {
                try
                {
                    Console.WriteLine($"Fetching HTML for {url} (attempt {i + 1}){(selector != null ? $" with selector '{selector}'" : "")}...");
                    var page = await browser.NewPageAsync();
                    await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    await page.GoToAsync(url, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, Timeout = 30000 });
                    if (!string.IsNullOrEmpty(selector))
                    {
                        await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = 10000 });
                    }
                    var html = await page.GetContentAsync();
                    await page.CloseAsync();
                    Console.WriteLine($"HTML fetched successfully (length: {html.Length}).");
                    return html;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fetch attempt {i + 1} failed: {ex.Message}");
                    if (i < retry - 1)
                    {
                        await Task.Delay(2000 * (i + 1));
                    }
                }
            }
            throw new Exception($"Failed to render {url} after {retry} retries.");
        }

        private static string GetSlugFromName(string name)
        {
            if (KnownSlugs.TryGetValue(name, out var slug)) return slug;
            var translit = name
                .Replace(" / ", "-")
                .Replace(" و ", "-va-")
                .Replace("(", "").Replace(")", "")
                .Replace("،", "");
            var latinMap = new Dictionary<string, string>
            {
                { "آ", "a" }, { "ا", "a" }, { "ب", "b" }, { "پ", "p" }, { "ت", "t" }, { "ث", "s" }, { "ج", "j" },
                { "چ", "ch" }, { "ح", "h" }, { "خ", "kh" }, { "د", "d" }, { "ذ", "z" }, { "ر", "r" },
                { "ز", "z" }, { "ژ", "zh" }, { "س", "s" }, { "ش", "sh" }, { "ص", "s" }, { "ض", "z" },
                { "ط", "t" }, { "ظ", "z" }, { "ع", "e" }, { "غ", "gh" }, { "ف", "f" }, { "ق", "q" },
                { "ک", "k" }, { "گ", "g" }, { "ل", "l" }, { "م", "m" }, { "ن", "n" }, { "و", "v" },
                { "ه", "h" }, { "ی", "y" }, { "ئ", "e" }, { "ء", "" }, { "ُ", "" }, { "ِ", "" }, { "َ", "" }
            };
            foreach (var kvp in latinMap)
            {
                translit = Regex.Replace(translit, Regex.Escape(kvp.Key), kvp.Value, RegexOptions.IgnoreCase);
            }
            var normalized = translit.Normalize(System.Text.NormalizationForm.FormD);
            return Regex.Replace(normalized.ToLowerInvariant(), @"[^a-z0-9\-]", "-").Trim('-').Replace("---", "-");
        }

        private static async Task<List<Job>> ScrapeJobsFromUrl(string url, string categoryName, string selector, bool fetchFullDesc)
        {
            var jobs = new List<Job>();
            try
            {
                var html = await GetRenderedHtmlAsync(url, selector);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                Console.WriteLine("HTML loaded into HtmlDocument for jobs.");
                var jobNodes = doc.DocumentNode.SelectNodes("//job-card[contains(@class, 'ng-star-inserted')]");
                Console.WriteLine($"Found {jobNodes?.Count ?? 0} job cards.");
                if (jobNodes == null) return jobs;
                foreach (var node in jobNodes) // Sequential jobs per page like original
                {
                    var job = new Job { Category = categoryName };
                    var linkNode = node.SelectSingleNode(".//a[contains(@class, 'tw-flex') and starts-with(@href, '/jobs/')]");
                    if (linkNode != null)
                    {
                        job.Link = new Uri(new Uri("https://jobvision.ir"), linkNode.GetAttributeValue("href", "")).ToString();
                        var titleNode = linkNode.SelectSingleNode(".//div[contains(@class, 'job-card-title')]");
                        job.Title = titleNode?.InnerText.Trim() ?? linkNode.GetAttributeValue("aria-label", "");
                    }
                    var companyNode = node.SelectSingleNode(".//a[starts-with(@href, '/companies/')]");
                    job.Company = companyNode?.InnerText.Trim() ?? "";
                    if (string.IsNullOrEmpty(job.Company))
                    {
                        var imgAltNode = node.SelectSingleNode(".//img[contains(@alt, 'شرکت') or contains(@alt, 'Company')]");
                        job.Company = imgAltNode?.GetAttributeValue("alt", "");
                    }
                    var locationNode = node.SelectSingleNode(".//span[contains(@class, 'tw-pointer-events-none')]");
                    job.Location = locationNode?.InnerText.Trim() ?? string.Empty;
                    var salaryNode = node.SelectSingleNode(".//span[contains(@class, 'label-01') and not(ancestor::span[contains(@class, 'filter-label')])]");
                    job.Salary = salaryNode?.InnerText.Trim() ?? string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[contains(@class, 'tw-text-gray-400')]");
                    job.PostedDate = dateNode?.InnerText.Trim() ?? string.Empty;
                    job.ShortDescription = linkNode?.GetAttributeValue("aria-label", job.Title);
                    job.FullDescription = fetchFullDesc ? await GetFullDescription(job.Link) : "Skipped for speed";
                    job.JobText = $"{job.Title} {job.Company} {job.Location} {job.Salary} {job.ShortDescription} {job.FullDescription}".Trim();
                    if (!string.IsNullOrEmpty(job.Title))
                    {
                        jobs.Add(job);
                    }
                    await Task.Delay(1000); // Reduced delay between jobs for less wait
                }
                var nextLi = doc.DocumentNode.SelectSingleNode("//li[contains(@class, 'pagination-page') and not(contains(@class, 'active'))][1]");
                if (nextLi != null)
                {
                    var nextA = nextLi.SelectSingleNode(".//a");
                    if (nextA != null)
                    {
                        var nextHref = nextA.GetAttributeValue("href", "");
                        Console.WriteLine($"Pagination detected. Next href: {nextHref}");
                    }
                }
                else
                {
                    Console.WriteLine("No pagination link found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scrape error for {url}: {ex.Message}");
            }
            return jobs;
        }

        private static async Task<string> GetFullDescription(string jobUrl)
        {
            if (string.IsNullOrEmpty(jobUrl)) return "";
            Console.WriteLine($"Fetching full desc for: {jobUrl}");
            var html = await GetRenderedHtmlAsync(jobUrl, null);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var descNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class, 'job-description') or contains(@class, 'details') or contains(@class, 'requirements') or contains(@class, 'position-info')] | " +
                "//section[contains(@class, 'description') or contains(@class, 'position')] | " +
                "//div[contains(@class, 'about-job') or contains(@class, 'about-company') or @class='job-details']");
            var text = descNode?.InnerText.Trim() ?? string.Empty;
            var snippet = text.Length > 0 ? text.Substring(0, Math.Min(200, text.Length)) + "..." : "Empty desc";
            Console.WriteLine($"Desc snippet: {snippet}");
            return Regex.Replace(text, @"\s{2,}", " ").Trim();
        }

        private static string GetNextPageUrl(string currentUrl)
        {
            var uri = new Uri(currentUrl);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var pageParam = query["page"];
            if (int.TryParse(pageParam, out var currentPage))
            {
                query["page"] = (currentPage + 1).ToString();
            }
            else
            {
                query["page"] = "2";
            }
            var basePath = uri.GetLeftPart(UriPartial.Path);
            return basePath + "?" + query.ToString();
        }

        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalidChars.Contains(c)).ToArray())
                   .Replace(" ", "_").Replace("/", "_").Replace("\\", "_").ToLowerInvariant();
        }
    }
}