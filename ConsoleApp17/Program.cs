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

        public int Slots => Type == RequestType.Passenger ? 1 : 2;
        public Direction GetDirection() => ToFloor > FromFloor ? Direction.Up : Direction.Down;
    }

    public class Elevator
    {
        public string Name;
        public int Capacity;
        public int CurrentFloor = 1;
        public int CurrentLoad = 0;
        public bool IsBusy = false;
        public double FreeAt = 0;

        public int TotalServed = 0;
        public double TotalBusyTime = 0;

        public Elevator(string name, int capacity) { Name = name; Capacity = capacity; }
        public double TravelTime(int from, int to) => Math.Abs(from - to) * 0.12;
        public bool CanTake(Request r) => !IsBusy && CurrentLoad + r.Slots <= Capacity;
    }

    class Program
    {
        static Random rand = new Random();
        static double time = 0;
        const double SIM_TIME = 600;
        static int nextId = 1;
        static int served = 0;
        static int lost = 0;
        static double totalWait = 0;
        static double totalServiceTime = 0;  // ← НОВАЯ ПЕРЕМЕННАЯ

        static List<Request> queue = new List<Request>();
        static List<Elevator> elevators = new List<Elevator>();
        static List<(double time, Action action)> events = new List<(double, Action)>();

        static double Exponential(double mean) => -Math.Log(1.0 - rand.NextDouble()) * mean;

        static void Main()
        {
            elevators.Add(new Elevator("Пассажирский", 6));
            elevators.Add(new Elevator("Грузовой", 13));

            Console.WriteLine("=== ЛИФТЫ (10 этажей, экспоненциальный поток) ===");
            Console.WriteLine($"Время симуляции: {SIM_TIME:F0} мин\n");

            for (int floor = 1; floor <= 10; floor++)
                ScheduleNextRequest(floor);

            while (time < SIM_TIME && events.Count > 0)
            {
                events.Sort((a, b) => a.time.CompareTo(b.time));
                var next = events[0];
                events.RemoveAt(0);
                time = next.time;
                next.action();
            }

            Console.WriteLine("\n========== РЕЗУЛЬТАТЫ ==========");
            Console.WriteLine($"Обслужено: {served}");
            Console.WriteLine($"Потеряно: {lost}");
            Console.WriteLine($"Вероятность отказа: {(double)lost / (served + lost + 0.0001):P1}");
            Console.WriteLine($"Среднее время ожидания: {(served > 0 ? totalWait / served : 0):F2} мин");
            Console.WriteLine($"Среднее время обслуживания: {(served > 0 ? totalServiceTime / served : 0):F2} мин");  // ← НОВЫЙ ВЫВОД

            foreach (var e in elevators)
            {
                double load = e.TotalBusyTime / SIM_TIME;
                Console.WriteLine($"\n{e.Name}:");
                Console.WriteLine($"  Обслужено: {e.TotalServed}");
                Console.WriteLine($"  Загрузка: {load:P1}");
            }
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

            queue.Add(req);
            Console.WriteLine($"[{time:F2}] + #{req.Id} {req.Type} {floor}→{toFloor} (очередь={queue.Sum(r => r.Slots)})");

            TryAssignElevators();
            ScheduleNextRequest(floor);
        }

        static void TryAssignElevators()
        {
            foreach (var e in elevators)
            {
                if (e.IsBusy || e.FreeAt > time + 0.001) continue;

                Request best = null;
                double bestDist = double.MaxValue;

                foreach (var r in queue)
                {
                    if (r.Type == RequestType.Cargo && e.Name != "Грузовой") continue;
                    if (e.CurrentLoad + r.Slots > e.Capacity) continue;

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
                    e.IsBusy = true;

                    double travelToPickup = e.TravelTime(e.CurrentFloor, best.FromFloor);
                    double travelToDest = e.TravelTime(best.FromFloor, best.ToFloor);
                    double pickupTime = time + travelToPickup;
                    double dropTime = pickupTime + travelToDest;

                    e.TotalBusyTime += travelToPickup + travelToDest;

                    Console.WriteLine($"[{time:F2}] 🚀 {e.Name} едет {e.CurrentFloor}→{best.FromFloor} за #{best.Id}");

                    AddEvent(pickupTime, () => PickupPassenger(e, best, pickupTime, dropTime));
                    e.FreeAt = dropTime;
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

            Console.WriteLine($"[{time:F2}] 🛑 {e.Name} забрал #{r.Id} на {r.FromFloor} этаже");

            AddEvent(dropTime, () => DropoffPassenger(e, r, dropTime));
        }

        static void DropoffPassenger(Elevator e, Request r, double dropTime)
        {
            time = dropTime;
            e.CurrentFloor = r.ToFloor;
            e.CurrentLoad -= r.Slots;
            r.FinishTime = time;

            double waitTime = r.BoardTime - r.CreateTime;
            double serviceTime = r.FinishTime - r.BoardTime;  // ← ВРЕМЯ ОБСЛУЖИВАНИЯ

            totalWait += waitTime;
            totalServiceTime += serviceTime;  // ← СУММИРУЕМ
            served++;
            e.TotalServed++;

            Console.WriteLine($"[{time:F2}] ✅ #{r.Id} доставлен {r.FromFloor}→{r.ToFloor} (ждал {waitTime:F2} мин, ехал {serviceTime:F2} мин)");

            e.IsBusy = false;
            e.FreeAt = 0;

            TryAssignElevators();
        }
    }
}