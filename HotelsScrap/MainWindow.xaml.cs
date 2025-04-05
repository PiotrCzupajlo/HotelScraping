using ClosedXML.Excel;
using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SeleniumExtras.WaitHelpers;
namespace HotelsScrap
{
    /// <summary>
    /// Logika interakcji dla klasy MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow() => InitializeComponent();

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string town = TownTextBox.Text.Trim();
            string link = tblink.Text;
            if (string.IsNullOrEmpty(town))
            {

                if (string.IsNullOrEmpty(link))
                {
                    MessageBox.Show("Please enter a town name or link.");
                    return;
                }
            }

            LogTextBox.Clear();
            AppendLog($"📍 Scraping hotels in: {town}");

            List<string> hotelNames = await Task.Run(() => ScrapeBooking(town,link));
            await Task.Run(() => GoogleSearchHotels(town, hotelNames));
        }

        void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"{message}\n");
                LogTextBox.ScrollToEnd();
            });
        }

        public class HotelInfo
        {
            public string Name { get; set; }
            public string PrimaryPhone { get; set; }
            public string Website { get; set; }
            public List<string> AdditionalPhones { get; set; } = new();
            public List<string> Emails { get; set; } = new();
        }

        List<string> ScrapeBooking(string town, string link)
        {
            List<string> hotelNames = new();
            string url = "";

            if (string.IsNullOrEmpty(link))
                url = $"https://www.booking.com/searchresults.pl.html?ss={Uri.EscapeDataString(town)}&nflt=ht_id%3D204";
            else if (string.IsNullOrEmpty(town))
                url = link;

            var options = new ChromeOptions();
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--start-maximized");

            using (IWebDriver driver = new ChromeDriver(options))
            {
                try
                {
                    driver.Navigate().GoToUrl(url);
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                    ScrollToEnd(driver);

                    // Repeatedly click "Load more results" if it appears
                    while (true)
                    {
                        try
                        {
                            var loadMoreButton = wait.Until(SeleniumExtras.WaitHelpers.ExpectedConditions
                                .ElementToBeClickable(By.XPath("//span[contains(text(), 'Load more results') or contains(text(), 'Załaduj więcej wyników')]/..")));

                            loadMoreButton.Click();
                            AppendLog("🔘 Clicked 'Load more results' button");

                            // Wait for content to load after clicking
                            Thread.Sleep(3000);
                            ScrollToEnd(driver);
                        }
                        catch (WebDriverTimeoutException)
                        {
                            AppendLog("✅ No more 'Load more results' buttons.");
                            break;
                        }
                    }

                    // Now scrape the hotel names
                    var hotels = driver.FindElements(By.CssSelector("div[data-testid='property-card']"));
                    foreach (var hotel in hotels)
                    {
                        try
                        {
                            string name = hotel.FindElement(By.CssSelector("div[data-testid='title']")).Text;
                            hotelNames.Add(name);
                            AppendLog($"🏨 Hotel: {name}");
                        }
                        catch (NoSuchElementException) { /* Skip if title not found */ }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"❌ Booking.com error: {ex.Message}");
                }
            }

            return hotelNames;
        }

        void ScrollToEnd(IWebDriver driver)
        {
            IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
            long lastHeight = (long)js.ExecuteScript("return document.body.scrollHeight");

            while (true)
            {
                js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                System.Threading.Thread.Sleep(1500);

                long newHeight = (long)js.ExecuteScript("return document.body.scrollHeight");
                if (newHeight == lastHeight) break;
                lastHeight = newHeight;
            }
        }

        async Task<string> GetHtmlAsync(string url)
        {
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                return await client.GetStringAsync(url);
            }
            catch
            {
                return string.Empty;
            }
        }

        (List<string> emails, List<string> phones) ExtractContactInfo(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            Thread.Sleep(1000);
            string text = doc.DocumentNode.InnerText;

            Regex emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            Regex phoneRegex = new Regex(@"(?:\+48[-\s]?)?(?:\d{2}[-\s]?\d{3}[-\s]?\d{2}[-\s]?\d{2}|\d{3}[-\s]?\d{3}[-\s]?\d{3})");

            var emails = emailRegex.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
            var phones = phoneRegex.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();


            return (emails, phones);
        }

        void GoogleSearchHotels(string town, List<string> hotelNames)
        {
            List<HotelInfo> allHotels = new();

            var options = new ChromeOptions();
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--start-maximized");

            using (IWebDriver driver = new ChromeDriver(options))
            {
                foreach (var hotel in hotelNames)
                {
                    var info = new HotelInfo { Name = hotel };
                    string query = $"{town} {hotel} kontakt";
                    string searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";

                    driver.Navigate().GoToUrl(searchUrl);
                    AppendLog($"🔍 Searching: {query}");
                    Thread.Sleep(1000); 
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));

                    try
                    {
                        var phoneElement = wait.Until(d => d.FindElement(By.XPath("//a[@data-dtype='d3ph']//span")));
                        info.PrimaryPhone = phoneElement.Text.Trim();
                    }
                    catch { info.PrimaryPhone = "N/A"; }


                        try
                        {
                            var searchResults = driver.FindElements(By.XPath("//div[@id='search']//a[@href]"));
                            foreach (var link in searchResults)
                            {
                                string href = link.GetAttribute("href");

                                if (string.IsNullOrEmpty(href)) continue;

                                // Skip known aggregators or irrelevant search links
                                if (href.Contains("booking.com") || href.Contains("expedia.com") ||
                                    href.Contains("tripadvisor.com") || href.Contains("hotels.com") ||
                                    href.Contains("/search?") || href.Contains("google.com") || href.Contains("webcache") || href.Contains("facebook"))
                                    continue;

                                // Heuristic: try to match any word from hotel name in the URL
                                var hotelWords = hotel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                bool matchFound = hotelWords.Any(word => href.ToLower().Contains(word.ToLower()));

                                if (matchFound)
                                {
                                    info.Website = href;
                                    break;
                                }
                            }
                        }
                        catch { info.Website = "N/A"; }


                        if (!string.IsNullOrWhiteSpace(info.Website) && info.Website != "N/A")
                    {
                        try
                        {
                            string html = GetHtmlAsync(info.Website).Result;
                            var (emails, phones) = ExtractContactInfo(html);
                            info.Emails.AddRange(emails);
                            info.AdditionalPhones.AddRange(phones);
                        }
                        catch { }
                    }

                    allHotels.Add(info);
                }
            }

            ExportToExcel(allHotels,town);
            AppendLog("✅ Excel file saved in C:\\Scraper\\");
        }

        void ExportToExcel(List<HotelInfo> hotels,string town)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Hotels");

            ws.Cell(1, 1).Value = "Hotel Name";
            ws.Cell(1, 2).Value = "Primary Phone";
            ws.Cell(1, 3).Value = "Website";
            ws.Cell(1, 4).Value = "Additional Phones";
            ws.Cell(1, 5).Value = "Emails";

            int row = 2;
            foreach (var hotel in hotels)
            {
                ws.Cell(row, 1).Value = hotel.Name;
                ws.Cell(row, 2).Value = hotel.PrimaryPhone;
                ws.Cell(row, 3).Value = hotel.Website;
                ws.Cell(row, 4).Value = string.Join(", ", hotel.AdditionalPhones);
                ws.Cell(row, 5).Value = string.Join(", ", hotel.Emails);
                row++;
            }

            wb.SaveAs(@"C:\Scraper\booking_hotels_"+town+".xlsx");

        }
    }
}
