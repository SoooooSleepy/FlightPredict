# Сайт для прогнозирования цены (ASP.NET Core), созданный для практики в создании сайтов и связке с CSV 
Был разработан для тренировки создания алгоритмов прогнозирования с привязкой к набору данных и также как первая практика в создании сайтов на ASP.NET Core.
Основная часть работы была проведена над алгоритмом прогнозирования представленном далее:

**Formatting Code for README**

The user wants to format code for their README in Git. I should explain how to wrap code in markdown using triple backticks, specifically for C#. Their code has some errors, like a missing assignment before the `.Where(...)` method and an undefined `distFromOrigin` variable. However, since they just need to insert the code, I'll keep it as-is but explain wrapping it correctly. I'll instruct them to replace Python's `'''` markers with ```csharp. Then, I'll provide the final markdown snippet.Вставьте в README так (Markdown):

```csharp
c#
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
        // Если маршрут есть, берем медианную цену
        basePrice = routeData.OrderBy(f => f.Price).ElementAt(routeData.Count / 2).Price;
    }
    else
    {
        // --- "ЭВРИСТИЧЕСКИЙ АНАЛИЗ ---
        methodNote = "(Интеллектуальный прогноз) ";

        // 1. Уточняем дистанцию (Геометрический поиск)
            .Where(f => f.Original.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(f => (long?)f.Distance).FirstOrDefault();

        var distToDest = allFlights
            .Where(f => f.Destination.Trim().Equals(destination.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(f => (long?)f.Distance).FirstOrDefault();

        long estimatedDistance = (distFromOrigin ?? distToDest ?? 1200); // 1200 - среднее по миру, если совсем пусто

        // 2. Рассчитываем "Рыночную стоимость километра" (Yield Management)
        var originStats = allFlights
         .Where(f => f.Original.Trim().Equals(origin.Trim(), StringComparison.OrdinalIgnoreCase) &&
                     (string.IsNullOrWhiteSpace(requestedClass) ||
                      f.FlightClass?.Trim().Equals(requestedClass, StringComparison.OrdinalIgnoreCase) == true))
         .ToList();

        decimal pricePerKm;
        if (originStats.Any())
        {
            pricePerKm = originStats.Average(f => f.Price / (f.Distance > 0 ? f.Distance : 1));
        }
        else
        {
            var sortedPpK = allFlights
                .Select(f => f.Price / (f.Distance > 0 ? f.Distance : 1))
                .OrderBy(x => x)
                .ToList();

            int skipCount = sortedPpK.Count / 10;
            pricePerKm = sortedPpK.Skip(skipCount).Take(sortedPpK.Count - 2 * skipCount).Average();
        }

        // 3. Фактор авиакомпании (Престижность)
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
            decimal airlineAvg = regionalAirlines.Average(f => f.Price / (f.Distance > 0 ? f.Distance : 1));
            airlineFactor = airlineAvg / (pricePerKm > 0 ? pricePerKm : 1);
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
```
