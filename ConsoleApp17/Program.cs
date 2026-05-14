using System;
using System.Collections.Generic;
using System.Linq;

namespace ElevatorSimulation
{
    public enum RequestType { Passenger, Cargo }
    public enum Direction { None, Up, Down }

    public class Request
    {
        public int Id;
        public RequestType Type;
        public int FromFloor;
        public int ToFloor;
        public double CreateTime;
        public double BoardTime;
        public double FinishTime;
        public bool IsLost = false;
        public string LostReason = "";

        public int Slots => Type == RequestType.Passenger ? 1 : 2;
        public Direction GetDirection() => ToFloor > FromFloor ? Direction.Up : Direction.Down;
    }

    public class Elevator
    {
        public string Name;
        public int Capacity;
        public int CurrentFloor = 1;
        public int CurrentLoad = 0;
        public Direction Direction = Direction.None;
        public bool IsMoving = false;
        public bool IsBroken = false;
        public double BrokenUntil = 0;
        public double ArrivalTime = 0;

        public int TotalServed = 0;
        public double TotalBusyTime = 0;

        public Elevator(string name, int capacity) { Name = name; Capacity = capacity; }
        public double TravelTime(int from, int to) => Math.Abs(from - to) * 0.12;
        public bool CanTake(Request r)
        {
            if (IsBroken) return false;
            if (r.Type == RequestType.Cargo && Name != "Грузовой") return false;
            return CurrentLoad + r.Slots <= Capacity;
        }
    }

    // Результат одного прогона
    class RunResult
    {
        public int Served;
        public int Lost;
        public int TotalRequests => Served + Lost;
        public double AvgWaitTimeMinutes;  // в минутах
        public double AvgServiceTimeMinutes;
        public double PassLoadPercent;
        public double CargoLoadPercent;
        public double TotalWaitTime;        // в минутах?
    }

    class Program
    {
        static Random rand = new Random();
        static double time = 0;
        const double SIM_TIME = 600; // 8 часов (480 мин), но у тебя 600???

        static int nextId = 1;
        static int served = 0;
        static int lost = 0;
        static double totalWait = 0;
        static double totalServiceTime = 0;

        // Буфер на 15 единиц
        static int BUFFER_SIZE = 15;
        static List<Request> queue = new List<Request>();
        static int CurrentBufferLoad => queue.Sum(r => r.Slots);

        static List<Elevator> elevators = new List<Elevator>();
        static List<(double time, Action action)> events = new List<(double, Action)>();

        // Возмущающие воздействия
        static bool powerOn = true;
        static double powerOffTime = 0;
        static bool powerEventScheduled = false;

        // Для статистики по прогону
        static void ResetSimulation()
        {
            time = 0;
            nextId = 1;
            served = 0;
            lost = 0;
            totalWait = 0;
            totalServiceTime = 0;
            queue.Clear();
            events.Clear();
            powerOn = true;
            powerOffTime = 0;
            powerEventScheduled = false;

            elevators.Clear();
            elevators.Add(new Elevator("Пассажирский", 6));
            elevators.Add(new Elevator("Грузовой", 13));
        }

        static double Exponential(double mean) => -Math.Log(1.0 - rand.NextDouble()) * mean;

        static void Main()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        ИМИТАЦИОННОЕ МОДЕЛИРОВАНИЕ: ПАССАЖИРСКО-ГРУЗОВОЙ        ║");
            Console.WriteLine("║                         ЛИФТОВОЙ СИСТЕМЫ                       ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // === ПАРАМЕТРЫ ДЛЯ СТАТИСТИЧЕСКОЙ УСТОЙЧИВОСТИ ===
            int N = 30;                // начальное число прогонов
            double eps = 0.05;         // точность 5%
            double tAlpha = 2.7;       // квантиль для 90% доверительной вероятности

            List<double> waitTimesList = new List<double>();   // средние времена ожидания по прогонам
            List<double> serviceTimesList = new List<double>();

            RunResult finalResult = new RunResult();

            Console.WriteLine($"--- ВЫПОЛНЯЕТСЯ {N} ПРОГОНОВ ПО {SIM_TIME / 60.0:F0} ЧАСОВ ---\n");

            for (int run = 1; run <= N; run++)
            {
                ResetSimulation();

                // Запускаем генерацию заявок
                for (int floor = 1; floor <= 10; floor++)
                    ScheduleNextRequest(floor);
                SchedulePowerEvent();

                while (time < SIM_TIME && events.Count > 0)
                {
                    events.Sort((a, b) => a.time.CompareTo(b.time));
                    var next = events[0];
                    events.RemoveAt(0);
                    time = next.time;
                    next.action();
                }

                // Сохраняем результаты прогона
                double avgWaitMin = (served > 0 ? totalWait / served : 0);
                double avgServiceMin = (served > 0 ? totalServiceTime / served : 0);

                waitTimesList.Add(avgWaitMin);
                serviceTimesList.Add(avgServiceMin);

                // Суммируем для финального результата
                finalResult.Served += served;
                finalResult.Lost += lost;
                finalResult.TotalWaitTime += totalWait;

                foreach (var e in elevators)
                {
                    if (e.Name == "Пассажирский")
                        finalResult.PassLoadPercent += e.TotalBusyTime / SIM_TIME * 100;
                    else
                        finalResult.CargoLoadPercent += e.TotalBusyTime / SIM_TIME * 100;
                }

                if (run % 10 == 0 || run == N)
                    Console.WriteLine($"  Прогон {run}/{N}: ср. ожидание = {avgWaitMin:F2} мин, обслужено = {served}, потеряно = {lost}");
            }

            // === РАСЧЁТ СТАТИСТИЧЕСКОЙ УСТОЙЧИВОСТИ ===
            double avgWait = waitTimesList.Average();
            double avgService = serviceTimesList.Average();

            // СКО = sqrt( sum(x_i - x̄)² / (n-1) )
            double stdDev = Math.Sqrt(waitTimesList.Sum(x => Math.Pow(x - avgWait, 2)) / (waitTimesList.Count - 1));

            // Необходимое число прогонов по формуле: N* = (tα² * σ²) / ε²
            double requiredRuns = Math.Pow(tAlpha, 2) * Math.Pow(stdDev, 2) / Math.Pow(eps, 2);

            // Доверительный интервал: x̄ ± tα * σ / √n
            double marginError = tAlpha * stdDev / Math.Sqrt(N);
            double ciLow = avgWait - marginError;
            double ciHigh = avgWait + marginError;

            Console.WriteLine("\n============================================================");
            Console.WriteLine("РЕЗУЛЬТАТЫ МОДЕЛИРОВАНИЯ: ПАССАЖИРСКО-ГРУЗОВОЙ ЛИФТ");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Моделирование завершено по времени: {SIM_TIME / 60.0:F0} час. ({SIM_TIME:F0} мин.)");
            Console.WriteLine($"Всего вошло в систему: {finalResult.TotalRequests / N}");
            Console.WriteLine($"Обслужено полностью: {finalResult.Served / N}");
            Console.WriteLine($"Потеряно всего: {finalResult.Lost / N}");

            double lossProb = (double)finalResult.Lost / (finalResult.Served + finalResult.Lost);
            Console.WriteLine($"Вероятность отказа: {lossProb:P1}");
            Console.WriteLine($"Среднее время ожидания: {avgWait:F2} мин.");
            Console.WriteLine($"Среднее время обслуживания (поездка): {avgService:F2} мин.");
            Console.WriteLine($"Загруженность пассажирского лифта: {finalResult.PassLoadPercent / N:F1}% от времени");
            Console.WriteLine($"Загруженность грузового лифта: {finalResult.CargoLoadPercent / N:F1}% от времени");

            Console.WriteLine("\n--- ОБЕСПЕЧЕНИЕ ТОЧНОСТИ (СТАТИСТИЧЕСКАЯ УСТОЙЧИВОСТЬ) ---");
            Console.WriteLine($"Всего выполнено прогонов: {N}");
            Console.WriteLine($"Среднее время ожидания: {avgWait:F2} мин.");
            Console.WriteLine($"Среднеквадратическое отклонение (σ): {stdDev:F3} мин.");
            Console.WriteLine($"Необходимое число реализаций (N*): {requiredRuns:F2}");
            Console.WriteLine($"Доверительный интервал для среднего (90%): [{ciLow:F3}; {ciHigh:F3}] мин.");

            if (requiredRuns <= N)
                Console.WriteLine("Точность обеспечена! Требуемое число прогонов меньше фактического.");
            else
                Console.WriteLine($"ВНИМАНИЕ: Точность НЕ обеспечена! Требуется {requiredRuns:F0} прогонов.");

           

            Console.ReadLine();
        }

        static void AddEvent(double eventTime, Action action)
        {
            events.Add((eventTime, action));
        }

        static void ScheduleNextRequest(int floor)
        {
            double mean = (floor == 1) ? 4.0 : 7.5;
            double interval = Exponential(mean);
            double nextTime = time + interval;

            if (nextTime < SIM_TIME)
            {
                AddEvent(nextTime, () => CreateRequest(floor, nextTime));
            }
        }

        static void CreateRequest(int floor, double eventTime)
        {
            time = eventTime;

            if (!powerOn)
            {
                var lostReq = new Request { Id = nextId++, Type = RequestType.Passenger, IsLost = true, LostReason = "отключение электричества" };
                lost++;
                ScheduleNextRequest(floor);
                return;
            }

            bool isPassenger = rand.NextDouble() < 0.7;
            int toFloor;
            do { toFloor = rand.Next(1, 11); } while (toFloor == floor);

            var req = new Request
            {
                Id = nextId++,
                Type = isPassenger ? RequestType.Passenger : RequestType.Cargo,
                FromFloor = floor,
                ToFloor = toFloor,
                CreateTime = time
            };

            if (CurrentBufferLoad + req.Slots <= BUFFER_SIZE)
            {
                req.CreateTime = time;
                queue.Add(req);
            }
            else if (req.Type == RequestType.Passenger)
            {
                req.IsLost = true;
                req.LostReason = "буфер переполнен";
                lost++;
            }
            else if (req.Type == RequestType.Cargo)
            {
                int removed = 0;
                for (int i = queue.Count - 1; i >= 0 && removed < 2; i--)
                {
                    if (queue[i].Type == RequestType.Passenger && !queue[i].IsLost)
                    {
                        queue[i].IsLost = true;
                        queue[i].LostReason = "вытеснен грузом";
                        lost++;
                        queue.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed == 2)
                {
                    queue.Add(req);
                }
                else
                {
                    req.IsLost = true;
                    req.LostReason = "нет пассажиров для вытеснения";
                    lost++;
                }
            }

            TryAssignElevators();
            ScheduleNextRequest(floor);
        }

        static void SchedulePowerEvent()
        {
            if (powerEventScheduled) return;
            powerEventScheduled = true;

            double interval = Exponential(120);
            double eventTime = time + interval;

            if (eventTime < SIM_TIME)
            {
                AddEvent(eventTime, () => TogglePower(eventTime));
            }
        }

        static void TogglePower(double eventTime)
        {
            time = eventTime;

            if (powerOn)
            {
                powerOn = false;
                double duration = Exponential(5);
                powerOffTime = time + duration;

                AddEvent(powerOffTime, () => TogglePower(powerOffTime));
            }
            else
            {
                powerOn = true;
                powerEventScheduled = false;
                SchedulePowerEvent();
            }
        }

        static void TryAssignElevators()
        {
            foreach (var e in elevators)
            {
                if (e.IsMoving || e.IsBroken) continue;
                if (queue.Count == 0) continue;

                Request best = null;
                double bestDist = double.MaxValue;

                foreach (var r in queue.Where(r => !r.IsLost))
                {
                    if (!e.CanTake(r)) continue;
                    double dist = Math.Abs(e.CurrentFloor - r.FromFloor);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = r;
                    }
                }

                if (best != null)
                {
                    queue.Remove(best);
                    e.IsMoving = true;

                    double travelToPickup = e.TravelTime(e.CurrentFloor, best.FromFloor);
                    double travelToDest = e.TravelTime(best.FromFloor, best.ToFloor);
                    double pickupTime = time + travelToPickup;
                    double dropTime = pickupTime + travelToDest;

                    e.TotalBusyTime += travelToPickup + travelToDest;

                    AddEvent(pickupTime, () => PickupPassenger(e, best, pickupTime, dropTime));
                    return;
                }
            }
        }

        static void PickupPassenger(Elevator e, Request r, double pickupTime, double dropTime)
        {
            time = pickupTime;
            e.CurrentFloor = r.FromFloor;
            e.CurrentLoad += r.Slots;
            r.BoardTime = time;
            e.Direction = r.GetDirection();

            AddEvent(dropTime, () => DropoffPassenger(e, r, dropTime));
        }

        static void DropoffPassenger(Elevator e, Request r, double dropTime)
        {
            time = dropTime;
            e.CurrentFloor = r.ToFloor;
            e.CurrentLoad -= r.Slots;
            r.FinishTime = time;

            double waitTime = r.BoardTime - r.CreateTime;
            double serviceTime = r.FinishTime - r.BoardTime;
            
            totalWait += waitTime;
            totalServiceTime += serviceTime;
            served++;
            e.TotalServed++;

            e.IsMoving = false;
            e.Direction = Direction.None;
            TryAssignElevators();
        }
    }
}