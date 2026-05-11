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

    class Program
    {
        static Random rand = new Random();
        static double time = 0;
        const double SIM_TIME = 600; // 8 часов

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

        static double Exponential(double mean) => -Math.Log(1.0 - rand.NextDouble()) * mean;

        static void Main()
        {
            elevators.Add(new Elevator("Пассажирский", 6));
            elevators.Add(new Elevator("Грузовой", 13));

            Console.WriteLine("=== ИМИТАЦИОННАЯ МОДЕЛЬ ЛИФТОВ ===");
            Console.WriteLine("10 этажей | 2 лифта | Буфер 15 ед.");
            Console.WriteLine($"Время симуляции: {SIM_TIME:F0} мин\n");

            // Запускаем генерацию заявок
            for (int floor = 1; floor <= 10; floor++)
                ScheduleNextRequest(floor);

            // Запускаем отключения электричества
            SchedulePowerEvent();

            while (time < SIM_TIME && events.Count > 0)
            {
                events.Sort((a, b) => a.time.CompareTo(b.time));
                var next = events[0];
                events.RemoveAt(0);
                time = next.time;
                next.action();
            }

            // Финальная статистика
            Console.WriteLine("\n========== РЕЗУЛЬТАТЫ ==========");
            Console.WriteLine($"Обслужено: {served}");
            Console.WriteLine($"Потеряно: {lost}");
            Console.WriteLine($"Вероятность отказа: {(double)lost / (served + lost + 0.0001):P1}");
            Console.WriteLine($"Среднее время ожидания: {(served > 0 ? totalWait / served : 0):F2} мин");
            Console.WriteLine($"Среднее время обслуживания: {(served > 0 ? totalServiceTime / served : 0):F2} мин");

            foreach (var e in elevators)
            {
                double load = e.TotalBusyTime / SIM_TIME;
                Console.WriteLine($"\n{e.Name}:");
                Console.WriteLine($"  Обслужено: {e.TotalServed}");
                Console.WriteLine($"  Загрузка: {load:P1}");
                Console.WriteLine($"  Состояние: {(e.IsBroken ? "СЛОМАН" : "Исправен")}");
            }
        }

        static void AddEvent(double eventTime, Action action)
        {
            events.Add((eventTime, action));
        }

        // Генерация следующий заявки на этаже
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

        // Создание новой заявки
        static void CreateRequest(int floor, double eventTime)
        {
            time = eventTime;

            // Проверка электричества
            if (!powerOn)
            {
                var lostReq = new Request { Id = nextId++, Type = RequestType.Passenger, IsLost = true, LostReason = "отключение электричества" };
                lost++;
                Console.WriteLine($"[{time:F2}] ❌ ПОТЕРЯ #{lostReq.Id} - электричество отключено");
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

            // ========== БУФЕР С ПРАВИЛОМ "ДОБРОЖЕЛАТЕЛЬНЫХ СОСЕДЕЙ" ==========

            // Если есть место в буфере
            if (CurrentBufferLoad + req.Slots <= BUFFER_SIZE)
            {
                req.CreateTime = time;
                queue.Add(req);
                Console.WriteLine($"[{time:F2}] + #{req.Id} {req.Type} {floor}→{toFloor} (буфер={CurrentBufferLoad}/{BUFFER_SIZE})");
            }
            // Если пассажир и места нет -> потеря
            else if (req.Type == RequestType.Passenger)
            {
                req.IsLost = true;
                req.LostReason = "буфер переполнен";
                lost++;
                Console.WriteLine($"[{time:F2}] ❌ ПОТЕРЯ #{req.Id} пассажир - буфер полон ({CurrentBufferLoad}/{BUFFER_SIZE})");
            }
            // Если груз и места нет -> вытесняем 2 пассажиров ("доброжелательные соседи")
            else if (req.Type == RequestType.Cargo)
            {
                // Ищем 2 пассажиров для вытеснения (с конца очереди - "доброжелательные соседи")
                int removed = 0;
                for (int i = queue.Count - 1; i >= 0 && removed < 2; i--)
                {
                    if (queue[i].Type == RequestType.Passenger && !queue[i].IsLost)
                    {
                        queue[i].IsLost = true;
                        queue[i].LostReason = "вытеснен грузом";
                        lost++;
                        Console.WriteLine($"[{time:F2}] ❌ ПОТЕРЯ #{queue[i].Id} пассажир - вытеснен грузом #{req.Id}");
                        queue.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed == 2)
                {
                    queue.Add(req);
                    Console.WriteLine($"[{time:F2}] + #{req.Id} ГРУЗ {floor}→{toFloor} (вытеснил 2 пассажиров, буфер={CurrentBufferLoad}/{BUFFER_SIZE})");
                }
                else
                {
                    req.IsLost = true;
                    req.LostReason = "нет пассажиров для вытеснения";
                    lost++;
                    Console.WriteLine($"[{time:F2}] ❌ ПОТЕРЯ #{req.Id} груз - нет пассажиров для вытеснения");
                }
            }

            // Пытаемся назначить лифты
            TryAssignElevators();

            // Следующая заявка
            ScheduleNextRequest(floor);
        }

        // Отключение электричества
        static void SchedulePowerEvent()
        {
            if (powerEventScheduled) return;
            powerEventScheduled = true;

            double interval = Exponential(120); // в среднем раз в 2 часа
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
                // Отключаем электричество
                powerOn = false;
                double duration = Exponential(5); // отключение на ~5 мин
                powerOffTime = time + duration;
                Console.WriteLine($"[{time:F2}] ⚡⚡⚡ ОТКЛЮЧЕНИЕ ЭЛЕКТРИЧЕСТВА на {duration:F2} мин");

                // Планируем включение
                AddEvent(powerOffTime, () => TogglePower(powerOffTime));
            }
            else
            {
                // Включаем электричество
                powerOn = true;
                Console.WriteLine($"[{time:F2}] ⚡ ВКЛЮЧЕНИЕ ЭЛЕКТРИЧЕСТВА");
                powerEventScheduled = false;
                SchedulePowerEvent();
            }
        }

        // Поломка лифта
        static void BreakElevator(Elevator e)
        {
            if (e.IsBroken) return;

            e.IsBroken = true;
            double duration = Exponential(10); // поломка на ~10 мин
            double repairTime = time + duration;

            Console.WriteLine($"[{time:F2}] 🔧 {e.Name} СЛОМАЛСЯ на {duration:F2} мин");

            AddEvent(repairTime, () => RepairElevator(e, repairTime));
        }

        static void RepairElevator(Elevator e, double repairTime)
        {
            time = repairTime;
            e.IsBroken = false;
            Console.WriteLine($"[{time:F2}] ✅ {e.Name} ОТРЕМОНТИРОВАН");
            TryAssignElevators();
        }

        // Назначение лифтов
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

                    Console.WriteLine($"[{time:F2}] 🚀 {e.Name} {e.CurrentFloor}→{best.FromFloor} за #{best.Id}");

                    AddEvent(pickupTime, () => PickupPassenger(e, best, pickupTime, dropTime));
                    return;
                }
            }
        }

        // Посадка
        static void PickupPassenger(Elevator e, Request r, double pickupTime, double dropTime)
        {
            time = pickupTime;
            e.CurrentFloor = r.FromFloor;
            e.CurrentLoad += r.Slots;
            r.BoardTime = time;
            e.Direction = r.GetDirection();

            Console.WriteLine($"[{time:F2}] 🛑 {e.Name} забрал #{r.Id} на {r.FromFloor} этаже (занято {e.CurrentLoad}/{e.Capacity})");

            AddEvent(dropTime, () => DropoffPassenger(e, r, dropTime));

            // Проверка на поломку (редкое событие)
            if (rand.NextDouble() < 0.01 && !e.IsBroken)
            {
                BreakElevator(e);
            }
        }

        // Высадка
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

            Console.WriteLine($"[{time:F2}] ✅ #{r.Id} {r.FromFloor}→{r.ToFloor} (ждал {waitTime:F2} мин, ехал {serviceTime:F2} мин)");

            e.IsMoving = false;
            e.Direction = Direction.None;

            TryAssignElevators();
        }
    }
}