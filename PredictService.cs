using CsvHelper;
using CsvHelper.Configuration;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;

namespace BlazorApp4
{
    public class PredictService
    {
        private readonly string _filePath;

        // Внедряем окружение через Dependency Injection
        public PredictService(IWebHostEnvironment env)
        {
            // Собираем путь к wwwroot/flights.csv
            _filePath = Path.Combine(env.WebRootPath, "flights.csv");
        }
        public ObservableCollection<FlightListItem> GetFlightsLists()
        {
            if (!System.IO.File.Exists(_filePath)) // Добавьте System.IO. перед File
            {
                return new ObservableCollection<FlightListItem>();
            }

            var config = new CsvConfiguration(new CultureInfo("ru-RU"))
            {
                HasHeaderRecord = true,
                Delimiter = ",",
        
                HeaderValidated = null,
                MissingFieldFound = null
            };
            using var reader = new StreamReader(_filePath);
            using var csv = new CsvReader(reader, config);


            csv.Context.RegisterClassMap<FlightMap>();

            var records = csv.GetRecords<FlightListItem>().ToList();

            return new ObservableCollection<FlightListItem>(records);
        }
        private class FlightMap : ClassMap<FlightListItem>
        {
            public FlightMap()
            {
                Map(m => m.Id).Name("Ticket_ID");
                Map(m => m.Airline).Name("Airline");
                Map(m => m.Original).Name("Origin");
                Map(m => m.OriginalCode).Name("Origin_code");
                Map(m => m.Destination).Name("Destination");
                Map(m => m.DestionationCode).Name("Destination_code");
                Map(m => m.Distance).Name("Distance_km");
                Map(m => m.FlightClass).Name("Class");

           
                Map(m => m.Price).Name("Price_RUB").Convert(args =>
                {
                    string raw = args.Row.GetField("Price_RUB");
                    if (string.IsNullOrWhiteSpace(raw)) return 0m;
                    var clean = new string(raw.Where(c => char.IsDigit(c) || c == ',').ToArray());
                    return decimal.TryParse(clean, out var res) ? res : 0m;
                });
            }
        }
        
        string DetermineRequestedClass(IEnumerable<FlightListItem> flights, string originCity, string destCity)
        {
            // 1) Попробуем найти самый частый класс для точного маршрута
            var classForRoute = flights
                .Where(f => f.Original?.Trim().Equals(originCity.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
                            f.Destination?.Trim().Equals(destCity.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
                            !string.IsNullOrWhiteSpace(f.FlightClass))
                .GroupBy(f => f.FlightClass.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(classForRoute)) return classForRoute;

            // 2) Иначе — самый частый класс из города вылета
            var classForOrigin = flights
                .Where(f => f.Original?.Trim().Equals(originCity.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
                            !string.IsNullOrWhiteSpace(f.FlightClass))
                .GroupBy(f => f.FlightClass.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            // 3) Если и этого нет — вернём пустую строку (означает "без фильтра по классу")
            return classForOrigin ?? "";
        }


        public (decimal Price, string Advice) PredictPrice(string origin, string destination, DateTime targetDate)
        {
            var allFlights = GetFlightsLists().ToList();
            if (!allFlights.Any()) return (0, "База данных полетов пуста.");
            string requestedClass = DetermineRequestedClass(allFlights, origin, destination);
            bool originExists = allFlights.Any(f => f.Original == origin);
            bool destinationExists = allFlights.Any(f => f.Destination == destination);

            if (!originExists || !destinationExists)
            {
                string missingCity = !originExists ? origin : destination;
                return (0, $"Ошибка: Город '{missingCity}' не найден в системе.");
            }
            // ПРОВЕРКА 2: Что пришло на вход и что есть в первой строке базы?
            var firstFlight = allFlights.First();
            Debug.WriteLine($"Ищем: '{origin}'->'{destination}'. В базе первый рейс: '{firstFlight.Original}'->'{firstFlight.Destination}'. ");


            // 1. Пытаемся найти исторические данные по конкретному маршруту

            var routeData = allFlights
            .Where(f =>
                f.Original?.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
                f.Destination?.Trim().Equals(destination.Trim(), StringComparison.OrdinalIgnoreCase) == true &&
                (string.IsNullOrWhiteSpace(requestedClass) ||
                 f.FlightClass?.Trim().Equals(requestedClass, StringComparison.OrdinalIgnoreCase) == true)
            )
            .ToList();
            decimal basePrice;
            string methodNote = "";

            if (routeData.Any())
            {
                // Если маршрут есть, берем медианную цену (она точнее среднего)
                basePrice = routeData.OrderBy(f => f.Price).ElementAt(routeData.Count / 2).Price;
            }
            else
            {
                // --- ГЛУБОКАЯ ЛОГИКА ПРОГНОЗИРОВАНИЯ (Маршрут-призрак) ---
                methodNote = "(Интеллектуальный прогноз) ";

                // 1. Уточняем дистанцию (Геометрический поиск)
                // Ищем дистанцию в базе: сначала рейсы из А в любые города, потом в Б из любых. 
                // Если находим оба, берем среднее между "плечами", если один - берем его.
                var distFromOrigin = allFlights
                    .Where(f => f.Original.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(f => (long?)f.Distance).FirstOrDefault();

                var distToDest = allFlights
                    .Where(f => f.Destination.Trim().Equals(destination.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(f => (long?)f.Distance).FirstOrDefault();

                long estimatedDistance = (distFromOrigin ?? distToDest ?? 1200); // 1200 - среднее по миру, если совсем пусто

                // 2. Рассчитываем "Рыночную стоимость километра" (Yield Management)
                // Мы не просто берем среднее, а смотрим, сколько стоят билеты из ЭТОГО города вылета
                // так как в разных аэропортах разные сборы и налоги.
                var originStats = allFlights
                 .Where(f => f.Original.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase) &&
                             (string.IsNullOrWhiteSpace(requestedClass) ||
                              f.FlightClass?.Trim().Equals(requestedClass, StringComparison.OrdinalIgnoreCase) == true))
                 .ToList();

                decimal pricePerKm;
                if (originStats.Any())
                {
                    // Считаем стоимость км именно для этого аэропорта вылета
                    pricePerKm = originStats.Average(f => f.Price / (f.Distance > 0 ? f.Distance : 1));
                }
                else
                {
                    // Если город вылета совсем новый, берем среднее по всей базе, но отсекаем 10% самых дорогих и дешевых (квартили)
                    var sortedPpK = allFlights
                        .Select(f => f.Price / (f.Distance > 0 ? f.Distance : 1))
                        .OrderBy(x => x)
                        .ToList();

                    int skipCount = sortedPpK.Count / 10;
                    pricePerKm = sortedPpK.Skip(skipCount).Take(sortedPpK.Count - 2 * skipCount).Average();
                }

                // 3. Фактор авиакомпании (Престижность)
                // Если в базе есть доминирующая компания для этого региона, применяем её средний коэффициент
                var regionalAirlines = allFlights
                  .Where(f => f.Original.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase) &&
                              (string.IsNullOrWhiteSpace(requestedClass) ||
                               f.FlightClass?.Trim().Equals(requestedClass, StringComparison.OrdinalIgnoreCase) == true))
                  .GroupBy(f => f.Airline)
                  .OrderByDescending(g => g.Count())
                  .FirstOrDefault();

                decimal airlineFactor = 1.0m;
                if (regionalAirlines != null)
                {
                    // Сравниваем среднюю цену этой авиакомпании с общим рынком
                    decimal airlineAvg = regionalAirlines.Average(f => f.Price / (f.Distance > 0 ? f.Distance : 1));
                    airlineFactor = airlineAvg / (pricePerKm > 0 ? pricePerKm : 1);

                    // Ограничиваем влияние авиакомпании, чтобы не было диких перекосов
                    airlineFactor = Math.Clamp(airlineFactor, 0.8m, 1.4m);
                }

                // Итоговая базовая цена
                basePrice = estimatedDistance * pricePerKm * airlineFactor;
            }

            // 2. Рассчитываем коэффициенты (Сезон, День, Срочность)
            decimal seasonality = GetSeasonalityFactor(targetDate.Month);

            decimal dayFactor = (targetDate.DayOfWeek == DayOfWeek.Friday ||
                                 targetDate.DayOfWeek == DayOfWeek.Sunday) ? 1.12m : 1.0m;

            int daysUntilFlight = (targetDate.Date - DateTime.Now.Date).Days;
            decimal bookingFactor = daysUntilFlight switch
            {
                < 3 => 1.6m,  // Критическая срочность
                < 10 => 1.3m,  // Позднее бронирование
                < 30 => 1.0m,  // Норма
                _ => 0.85m  // Раннее бронирование (скидка)
            };

            // 3. Итоговый расчет
            decimal predictedPrice = Math.Round(basePrice * seasonality * dayFactor * bookingFactor, 2);

            // 4. Формируем умный совет
            // Сравниваем предсказание с "нормальной" ценой (без учета срочности и сезона)
            decimal priceDiffRatio = predictedPrice / basePrice;

            string advice = priceDiffRatio switch
            {
                < 0.90m => $"🔥 {methodNote}Сейчас уникально низкая цена! Рекомендуем покупать немедленно.",
                <= 1.10m => $"✅ {methodNote}Цена соответствует рыночному прогнозу. Хорошее время для поездки.",
                <= 1.30m => $"⚠️ {methodNote}Цена завышена из-за факторов спроса. Если даты гибкие, лучше подождать.",
                _ => $"❌ {methodNote}Очень невыгодный период. Попробуйте сдвинуть дату на 2-3 недели."
            };

            return (predictedPrice, advice);
        }
        private decimal GetSeasonalityFactor(int month)
        {
            return month switch
            {
                12 or 1 or 7 or 8 => 1.3m,  // Пик: Новый год и лето
                5 or 6 or 9 => 1.1m,       // Майские и бархатный сезон
                2 or 11 => 0.8m,           // Глухое межсезонье
                _ => 1.0m                  // Остальные месяцы
            };
        }
    }
    public class FlightListItem
    {
        // Пустой конструктор ОБЯЗАТЕЛЕН для CsvHelper
        public FlightListItem() { }
        public FlightListItem(int id, string airline, string original, string originalCode,
                              string destination, string destinationCode, long distance,
                              string flightClass, decimal price)
        {
            Id = id; Airline = airline; Original = original; OriginalCode = originalCode;
            Destination = destination; DestionationCode = destinationCode;
            Distance = distance; FlightClass = flightClass; Price = price;
        }

        public int Id { get; set; }
        public string Airline { get; set; }
        public string Original { get; set; }
        public string OriginalCode { get; set; }
        public string Destination { get; set; }
        public string DestionationCode { get; set; }
        public long Distance { get; set; }
        public string FlightClass { get; set; }
        public decimal Price { get; set; }
    }
}
