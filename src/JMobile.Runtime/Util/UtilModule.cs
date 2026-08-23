using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;

namespace JMobile.Runtime.Util;

/// <summary>
/// The <c>java.util</c> classes CLDC defines: the collections, Random, the date and time types,
/// and the timer.
/// </summary>
public sealed class UtilModule : IRuntimeModule
{
    public string Name => "java.util";

    public void Register(RuntimeRegistry registry)
    {
        registry.DefineThrowable(WellKnown.NoSuchElementException, WellKnown.RuntimeException);
        registry.DefineThrowable("java/util/EmptyStackException", WellKnown.RuntimeException);

        RegisterEnumeration(registry);
        RegisterVector(registry);
        RegisterStack(registry);
        RegisterHashtable(registry);
        RegisterRandom(registry);
        RegisterDateAndCalendar(registry);
        RegisterTimer(registry);
    }

    // ---------------------------------------------------------------------------------
    // Enumeration
    // ---------------------------------------------------------------------------------

    private static void RegisterEnumeration(RuntimeRegistry registry)
    {
        registry.DefineInterface("java/util/Enumeration", builder => builder
            .InterfaceMethod("hasMoreElements", "()Z")
            .InterfaceMethod("nextElement", "()Ljava/lang/Object;"));

        // A concrete enumeration the library hands back from elements(), keys() and so on.
        registry.Define("java/util/ArrayEnumeration", builder => builder
            .Implements("java/util/Enumeration")
            .AllocatesWith(static type => new JavaEnumeration(type, type.InstanceFieldCount))
            .Method("hasMoreElements", "()Z",
                static (_, args) => JavaValue.Bool(args.Self<JavaEnumeration>().HasMore))
            .Method("nextElement", "()Ljava/lang/Object;", static (thread, args) =>
            {
                var self = args.Self<JavaEnumeration>();
                if (!self.HasMore) throw thread.Vm.NewNoSuchElementException(thread);
                return JavaValue.Ref(self.Snapshot[self.Cursor++]);
            }));
    }

    private static JavaValue NewEnumeration(JavaThread thread, JavaObject?[] snapshot)
    {
        JavaClass type = thread.Vm.LoadClass("java/util/ArrayEnumeration");
        var enumeration = (JavaEnumeration)thread.Vm.NewInstance(type);
        enumeration.Snapshot = snapshot;
        return JavaValue.Ref(enumeration);
    }

    // ---------------------------------------------------------------------------------
    // Vector
    // ---------------------------------------------------------------------------------

    private static void RegisterVector(RuntimeRegistry registry)
    {
        registry.Define("java/util/Vector", builder => builder
            .AllocatesWith(static type => new JavaVector(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(I)V", static (_, args) =>
            {
                args.Self<JavaVector>().Items.Capacity = Math.Max(0, args.Int(0));
                return JavaValue.Void;
            })
            .Constructor("(II)V", static (_, args) =>
            {
                var self = args.Self<JavaVector>();
                self.Items.Capacity = Math.Max(0, args.Int(0));
                self.CapacityIncrement = args.Int(1);
                return JavaValue.Void;
            })
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaVector>().Items.Count))
            .Method("isEmpty", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaVector>().Items.Count == 0))
            .Method("capacity", "()I", static (_, args) => JavaValue.Int(args.Self<JavaVector>().Items.Capacity))
            .Method("addElement", "(Ljava/lang/Object;)V", static (_, args) =>
            {
                args.Self<JavaVector>().Items.Add(args.Object(0));
                return JavaValue.Void;
            })
            .Method("elementAt", "(I)Ljava/lang/Object;", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                int index = args.Int(0);
                if ((uint)index >= (uint)items.Count)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, index, items.Count);
                return JavaValue.Ref(items[index]);
            })
            .Method("firstElement", "()Ljava/lang/Object;", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                if (items.Count == 0) throw thread.Vm.NewNoSuchElementException(thread);
                return JavaValue.Ref(items[0]);
            })
            .Method("lastElement", "()Ljava/lang/Object;", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                if (items.Count == 0) throw thread.Vm.NewNoSuchElementException(thread);
                return JavaValue.Ref(items[^1]);
            })
            .Method("setElementAt", "(Ljava/lang/Object;I)V", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                int index = args.Int(1);
                if ((uint)index >= (uint)items.Count)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, index, items.Count);
                items[index] = args.Object(0);
                return JavaValue.Void;
            })
            .Method("insertElementAt", "(Ljava/lang/Object;I)V", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                int index = args.Int(1);
                if ((uint)index > (uint)items.Count)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, index, items.Count);
                items.Insert(index, args.Object(0));
                return JavaValue.Void;
            })
            .Method("removeElementAt", "(I)V", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                int index = args.Int(0);
                if ((uint)index >= (uint)items.Count)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, index, items.Count);
                items.RemoveAt(index);
                return JavaValue.Void;
            })
            .Method("removeElement", "(Ljava/lang/Object;)Z", static (thread, args) =>
            {
                var self = args.Self<JavaVector>();
                int index = IndexOf(thread, self.Items, args.Object(0), 0);
                if (index < 0) return JavaValue.Bool(false);
                self.Items.RemoveAt(index);
                return JavaValue.Bool(true);
            })
            .Method("removeAllElements", "()V", static (_, args) =>
            {
                args.Self<JavaVector>().Items.Clear();
                return JavaValue.Void;
            })
            .Method("contains", "(Ljava/lang/Object;)Z", static (thread, args) =>
                JavaValue.Bool(IndexOf(thread, args.Self<JavaVector>().Items, args.Object(0), 0) >= 0))
            .Method("indexOf", "(Ljava/lang/Object;)I", static (thread, args) =>
                JavaValue.Int(IndexOf(thread, args.Self<JavaVector>().Items, args.Object(0), 0)))
            .Method("indexOf", "(Ljava/lang/Object;I)I", static (thread, args) =>
                JavaValue.Int(IndexOf(thread, args.Self<JavaVector>().Items, args.Object(0), args.Int(1))))
            .Method("lastIndexOf", "(Ljava/lang/Object;)I", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                JavaObject? needle = args.Object(0);
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (ElementsEqual(thread, items[i], needle)) return JavaValue.Int(i);
                }
                return JavaValue.Int(-1);
            })
            .Method("ensureCapacity", "(I)V", static (_, args) =>
            {
                var self = args.Self<JavaVector>();
                int wanted = args.Int(0);
                if (wanted > self.Items.Capacity) self.Items.Capacity = wanted;
                return JavaValue.Void;
            })
            .Method("setSize", "(I)V", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                int size = args.Int(0);
                if (size < 0) throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, size, items.Count);

                while (items.Count > size) items.RemoveAt(items.Count - 1);
                while (items.Count < size) items.Add(null);
                return JavaValue.Void;
            })
            .Method("trimToSize", "()V", static (_, args) =>
            {
                args.Self<JavaVector>().Items.TrimExcess();
                return JavaValue.Void;
            })
            .Method("copyInto", "([Ljava/lang/Object;)V", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                var target = args.Object<JavaArray<JavaObject?>>(0)
                             ?? throw thread.Vm.NewNullPointerException(thread, "anArray");
                if (target.Length < items.Count)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, items.Count, target.Length);

                for (int i = 0; i < items.Count; i++) target.Items[i] = items[i];
                return JavaValue.Void;
            })
            .Method("elements", "()Ljava/util/Enumeration;",
                static (thread, args) => NewEnumeration(thread, args.Self<JavaVector>().Items.ToArray()))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, $"[Vector size={args.Self<JavaVector>().Items.Count}]")));
    }

    /// <summary>
    /// Finds an element using Java equality, which means calling the element's own
    /// <c>equals</c> unless the search key is null.
    /// </summary>
    private static int IndexOf(JavaThread thread, List<JavaObject?> items, JavaObject? needle, int from)
    {
        for (int i = Math.Max(0, from); i < items.Count; i++)
        {
            if (ElementsEqual(thread, items[i], needle)) return i;
        }
        return -1;
    }

    private static bool ElementsEqual(JavaThread thread, JavaObject? element, JavaObject? needle)
    {
        if (ReferenceEquals(element, needle)) return true;
        if (needle is null || element is null) return false;

        if (needle is JavaString needleText)
            return element is JavaString elementText &&
                   string.Equals(needleText.Value, elementText.Value, StringComparison.Ordinal);

        return thread.Vm.CallVirtual(thread, needle, "equals", "(Ljava/lang/Object;)Z",
            JavaValue.Ref(element)).AsBool;
    }

    // ---------------------------------------------------------------------------------
    // Stack
    // ---------------------------------------------------------------------------------

    private static void RegisterStack(RuntimeRegistry registry)
    {
        registry.Define("java/util/Stack", builder => builder
            .Extends("java/util/Vector")
            .AllocatesWith(static type => new JavaVector(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("push", "(Ljava/lang/Object;)Ljava/lang/Object;", static (_, args) =>
            {
                args.Self<JavaVector>().Items.Add(args.Object(0));
                return JavaValue.Ref(args.Object(0));
            })
            .Method("pop", "()Ljava/lang/Object;", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                if (items.Count == 0)
                    throw thread.Vm.NewException(thread, "java/util/EmptyStackException", null);

                JavaObject? top = items[^1];
                items.RemoveAt(items.Count - 1);
                return JavaValue.Ref(top);
            })
            .Method("peek", "()Ljava/lang/Object;", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                if (items.Count == 0)
                    throw thread.Vm.NewException(thread, "java/util/EmptyStackException", null);
                return JavaValue.Ref(items[^1]);
            })
            .Method("empty", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaVector>().Items.Count == 0))
            .Method("search", "(Ljava/lang/Object;)I", static (thread, args) =>
            {
                List<JavaObject?> items = args.Self<JavaVector>().Items;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (ElementsEqual(thread, items[i], args.Object(0)))
                        return JavaValue.Int(items.Count - i);
                }
                return JavaValue.Int(-1);
            }));
    }

    // ---------------------------------------------------------------------------------
    // Hashtable
    // ---------------------------------------------------------------------------------

    private static void RegisterHashtable(RuntimeRegistry registry)
    {
        registry.Define("java/util/Hashtable", builder => builder
            .AllocatesWith(static type => new JavaHashtable(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Constructor("(I)V", static (_, _) => JavaValue.Void)
            .Method("size", "()I", static (_, args) => JavaValue.Int(args.Self<JavaHashtable>().Count))
            .Method("isEmpty", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaHashtable>().Count == 0))
            .Method("get", "(Ljava/lang/Object;)Ljava/lang/Object;", static (thread, args) =>
            {
                JavaObject key = RuntimeSupport.RequireRef(thread, args.Object(0), "key");
                return JavaValue.Ref(args.Self<JavaHashtable>().Get(thread, key));
            })
            .Method("put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", static (thread, args) =>
            {
                JavaObject key = RuntimeSupport.RequireRef(thread, args.Object(0), "key");
                // Java's Hashtable rejects null values outright rather than storing them.
                JavaObject value = RuntimeSupport.RequireRef(thread, args.Object(1), "value");
                return JavaValue.Ref(args.Self<JavaHashtable>().Put(thread, key, value));
            })
            .Method("remove", "(Ljava/lang/Object;)Ljava/lang/Object;", static (thread, args) =>
            {
                JavaObject key = RuntimeSupport.RequireRef(thread, args.Object(0), "key");
                return JavaValue.Ref(args.Self<JavaHashtable>().Remove(thread, key));
            })
            .Method("containsKey", "(Ljava/lang/Object;)Z", static (thread, args) =>
            {
                JavaObject key = RuntimeSupport.RequireRef(thread, args.Object(0), "key");
                return JavaValue.Bool(args.Self<JavaHashtable>().ContainsKey(thread, key));
            })
            .Method("contains", "(Ljava/lang/Object;)Z", static (thread, args) =>
            {
                JavaObject value = RuntimeSupport.RequireRef(thread, args.Object(0), "value");
                foreach (JavaHashtable.Entry entry in args.Self<JavaHashtable>().Entries)
                {
                    if (ElementsEqual(thread, entry.Value, value)) return JavaValue.Bool(true);
                }
                return JavaValue.Bool(false);
            })
            .Method("clear", "()V", static (_, args) =>
            {
                args.Self<JavaHashtable>().Clear();
                return JavaValue.Void;
            })
            .Method("keys", "()Ljava/util/Enumeration;", static (thread, args) =>
                NewEnumeration(thread,
                    args.Self<JavaHashtable>().Entries.Select(entry => (JavaObject?)entry.Key).ToArray()))
            .Method("elements", "()Ljava/util/Enumeration;", static (thread, args) =>
                NewEnumeration(thread,
                    args.Self<JavaHashtable>().Entries.Select(entry => entry.Value).ToArray()))
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread, $"[Hashtable size={args.Self<JavaHashtable>().Count}]")));
    }

    // ---------------------------------------------------------------------------------
    // Random
    // ---------------------------------------------------------------------------------

    private static void RegisterRandom(RuntimeRegistry registry)
    {
        registry.Define("java/util/Random", builder => builder
            .AllocatesWith(static type => new JavaRandom(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, args) =>
            {
                args.Self<JavaRandom>().SeedFromClock();
                return JavaValue.Void;
            })
            .Constructor("(J)V", static (_, args) =>
            {
                args.Self<JavaRandom>().SetSeed(args.Long(0));
                return JavaValue.Void;
            })
            .Method("setSeed", "(J)V", static (_, args) =>
            {
                args.Self<JavaRandom>().SetSeed(args.Long(0));
                return JavaValue.Void;
            })
            .Method("nextInt", "()I", static (_, args) => JavaValue.Int(args.Self<JavaRandom>().NextInt()))
            .Method("nextInt", "(I)I", static (thread, args) =>
            {
                int bound = args.Int(0);
                if (bound <= 0) throw thread.Vm.NewIllegalArgumentException(thread, "bound must be positive");
                return JavaValue.Int(args.Self<JavaRandom>().NextInt(bound));
            })
            .Method("nextLong", "()J", static (_, args) => JavaValue.Long(args.Self<JavaRandom>().NextLong()))
            .Method("nextBoolean", "()Z", static (_, args) => JavaValue.Bool(args.Self<JavaRandom>().NextBoolean()))
            .Method("nextFloat", "()F", static (_, args) => JavaValue.Float(args.Self<JavaRandom>().NextFloat()))
            .Method("nextDouble", "()D", static (_, args) => JavaValue.Double(args.Self<JavaRandom>().NextDouble())));
    }

    // ---------------------------------------------------------------------------------
    // Date, Calendar and TimeZone
    // ---------------------------------------------------------------------------------

    private static void RegisterDateAndCalendar(RuntimeRegistry registry)
    {
        registry.Define("java/util/Date", builder => builder
            .Field("time", "J")
            .Constructor("()V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return JavaValue.Void;
            })
            .Constructor("(J)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(args.Long(0)));
                return JavaValue.Void;
            })
            .Method("getTime", "()J", static (_, args) => args.This.GetField(0))
            .Method("setTime", "(J)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(args.Long(0)));
                return JavaValue.Void;
            })
            .Method("equals", "(Ljava/lang/Object;)Z", static (_, args) =>
            {
                var other = args.Object(0);
                return JavaValue.Bool(other is not null && other.Class.Name == "java/util/Date" &&
                                      other.GetField(0).AsLong == args.This.GetField(0).AsLong);
            })
            .Method("hashCode", "()I", static (_, args) =>
            {
                long time = args.This.GetField(0).AsLong;
                return JavaValue.Int((int)(time ^ (long)((ulong)time >> 32)));
            })
            .Method("toString", "()Ljava/lang/String;", static (thread, args) =>
                RuntimeSupport.Str(thread,
                    DateTimeOffset.FromUnixTimeMilliseconds(args.This.GetField(0).AsLong)
                        .UtcDateTime.ToString("ddd MMM dd HH:mm:ss 'UTC' yyyy"))));

        registry.Define("java/util/TimeZone", builder => builder
            .AsAbstract()
            .DefaultConstructor()
            .StaticMethod("getDefault", "()Ljava/util/TimeZone;", static (thread, _) =>
                JavaValue.Ref(thread.Vm.Construct(thread, thread.Vm.LoadClass("java/util/SimpleTimeZone"), "()V")))
            .StaticMethod("getTimeZone", "(Ljava/lang/String;)Ljava/util/TimeZone;", static (thread, _) =>
                JavaValue.Ref(thread.Vm.Construct(thread, thread.Vm.LoadClass("java/util/SimpleTimeZone"), "()V")))
            .Method("getID", "()Ljava/lang/String;", static (thread, _) => RuntimeSupport.Str(thread, "UTC"))
            .Method("getRawOffset", "()I", static (_, _) => JavaValue.Int(0)));

        registry.Define("java/util/SimpleTimeZone", builder => builder
            .Extends("java/util/TimeZone")
            .Constructor("()V", static (_, _) => JavaValue.Void));

        // Calendar field constants, which games index directly.
        registry.Define("java/util/Calendar", builder => builder
            .AsAbstract()
            .Field("millis", "J")
            .Constant("YEAR", 1)
            .Constant("MONTH", 2)
            .Constant("DAY_OF_MONTH", 5)
            .Constant("DATE", 5)
            .Constant("DAY_OF_WEEK", 7)
            .Constant("AM_PM", 9)
            .Constant("HOUR", 10)
            .Constant("HOUR_OF_DAY", 11)
            .Constant("MINUTE", 12)
            .Constant("SECOND", 13)
            .Constant("MILLISECOND", 14)
            .Constant("SUNDAY", 1)
            .Constant("MONDAY", 2)
            .Constant("TUESDAY", 3)
            .Constant("WEDNESDAY", 4)
            .Constant("THURSDAY", 5)
            .Constant("FRIDAY", 6)
            .Constant("SATURDAY", 7)
            .Constant("JANUARY", 0)
            .Constant("AM", 0)
            .Constant("PM", 1)
            .Constructor("()V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return JavaValue.Void;
            })
            .StaticMethod("getInstance", "()Ljava/util/Calendar;", static (thread, _) =>
                JavaValue.Ref(thread.Vm.Construct(thread, thread.Vm.LoadClass("java/util/GregorianCalendar"), "()V")))
            .StaticMethod("getInstance", "(Ljava/util/TimeZone;)Ljava/util/Calendar;", static (thread, _) =>
                JavaValue.Ref(thread.Vm.Construct(thread, thread.Vm.LoadClass("java/util/GregorianCalendar"), "()V")))
            .Method("getTime", "()Ljava/util/Date;", static (thread, args) =>
                JavaValue.Ref(thread.Vm.Construct(thread, thread.Vm.LoadClass("java/util/Date"), "(J)V",
                    args.This.GetField(0))))
            .Method("setTime", "(Ljava/util/Date;)V", static (thread, args) =>
            {
                JavaObject date = RuntimeSupport.RequireRef(thread, args.Object(0), "date");
                args.This.SetField(0, date.GetField(0));
                return JavaValue.Void;
            })
            .Method("setTimeInMillis", "(J)V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(args.Long(0)));
                return JavaValue.Void;
            })
            .Method("getTimeInMillis", "()J", static (_, args) => args.This.GetField(0))
            .Method("get", "(I)I", static (_, args) =>
            {
                DateTime moment = DateTimeOffset
                    .FromUnixTimeMilliseconds(args.This.GetField(0).AsLong).UtcDateTime;

                return JavaValue.Int(args.Int(0) switch
                {
                    1 => moment.Year,
                    // Java months are zero-based, which is a classic source of off-by-one bugs
                    // in game code that we must reproduce rather than correct.
                    2 => moment.Month - 1,
                    5 => moment.Day,
                    7 => (int)moment.DayOfWeek + 1,
                    9 => moment.Hour < 12 ? 0 : 1,
                    10 => moment.Hour % 12,
                    11 => moment.Hour,
                    12 => moment.Minute,
                    13 => moment.Second,
                    14 => moment.Millisecond,
                    _ => 0,
                });
            })
            .Method("set", "(II)V", static (_, args) =>
            {
                DateTime moment = DateTimeOffset
                    .FromUnixTimeMilliseconds(args.This.GetField(0).AsLong).UtcDateTime;
                int value = args.Int(1);

                DateTime updated = args.Int(0) switch
                {
                    1 => moment.AddYears(value - moment.Year),
                    2 => moment.AddMonths(value + 1 - moment.Month),
                    5 => moment.AddDays(value - moment.Day),
                    11 => moment.AddHours(value - moment.Hour),
                    12 => moment.AddMinutes(value - moment.Minute),
                    13 => moment.AddSeconds(value - moment.Second),
                    14 => moment.AddMilliseconds(value - moment.Millisecond),
                    _ => moment,
                };

                args.This.SetField(0, JavaValue.Long(new DateTimeOffset(updated, TimeSpan.Zero)
                    .ToUnixTimeMilliseconds()));
                return JavaValue.Void;
            }));

        registry.Define("java/util/GregorianCalendar", builder => builder
            .Extends("java/util/Calendar")
            .Constructor("()V", static (_, args) =>
            {
                args.This.SetField(0, JavaValue.Long(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return JavaValue.Void;
            }));
    }

    // ---------------------------------------------------------------------------------
    // Timer
    // ---------------------------------------------------------------------------------

    private static void RegisterTimer(RuntimeRegistry registry)
    {
        registry.Define("java/util/TimerTask", builder => builder
            .AsAbstract()
            .Implements(WellKnown.Runnable)
            .Field("cancelled", "Z")
            .Field("scheduledTime", "J")
            .DefaultConstructor()
            .AbstractMethod("run", "()V")
            .Method("cancel", "()Z", static (_, args) =>
            {
                bool wasActive = !args.This.GetField(0).AsBool;
                args.This.SetField(0, JavaValue.Bool(true));
                return JavaValue.Bool(wasActive);
            })
            .Method("scheduledExecutionTime", "()J", static (_, args) => args.This.GetField(1)));

        registry.Define("java/util/Timer", builder => builder
            .AllocatesWith(static type => new JavaTimer(type, type.InstanceFieldCount))
            .Constructor("()V", static (_, _) => JavaValue.Void)
            .Method("schedule", "(Ljava/util/TimerTask;J)V", static (thread, args) =>
            {
                Schedule(thread, args.Self<JavaTimer>(), args.Object(0), args.Long(1), -1);
                return JavaValue.Void;
            })
            .Method("schedule", "(Ljava/util/TimerTask;JJ)V", static (thread, args) =>
            {
                Schedule(thread, args.Self<JavaTimer>(), args.Object(0), args.Long(1), args.Long(2));
                return JavaValue.Void;
            })
            .Method("scheduleAtFixedRate", "(Ljava/util/TimerTask;JJ)V", static (thread, args) =>
            {
                Schedule(thread, args.Self<JavaTimer>(), args.Object(0), args.Long(1), args.Long(2));
                return JavaValue.Void;
            })
            .Method("cancel", "()V", static (_, args) =>
            {
                args.Self<JavaTimer>().Cancel();
                return JavaValue.Void;
            }));
    }

    /// <summary>
    /// Schedules a task on the timer's own thread.
    /// </summary>
    /// <remarks>
    /// Games commonly drive their whole main loop from a repeating <c>TimerTask</c>, so the task
    /// has to run on a genuine Java thread with its own interpreter stack rather than on a
    /// thread pool callback.
    /// </remarks>
    private static void Schedule(JavaThread caller, JavaTimer timer, JavaObject? task, long delay, long period)
    {
        if (task is null) throw caller.Vm.NewNullPointerException(caller, "task");

        VirtualMachine vm = caller.Vm;
        JavaThread execution = vm.CreateThread("Timer");

        timer.Start(execution, task, delay, period, vm);
    }
}

/// <summary>A <c>java.util.Timer</c> and the worker threads its tasks run on.</summary>
public sealed class JavaTimer(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<Thread> _workers = [];

    public void Start(JavaThread execution, JavaObject task, long delay, long period, VirtualMachine vm)
    {
        CancellationToken token = _cancellation.Token;

        var worker = new Thread(() =>
        {
            try
            {
                if (delay > 0 && token.WaitHandle.WaitOne((int)Math.Min(delay, int.MaxValue))) return;

                while (!token.IsCancellationRequested && !execution.AbortRequested)
                {
                    // A cancelled task stops the schedule, matching TimerTask.cancel.
                    if (task.GetField(0).AsBool) return;

                    task.SetField(1, JavaValue.Long(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    vm.CallVirtual(execution, task, "run", "()V");

                    if (period <= 0) return;
                    if (token.WaitHandle.WaitOne((int)Math.Min(period, int.MaxValue))) return;
                }
            }
            catch (JavaThrowable error)
            {
                vm.Options.StandardError?.Invoke(
                    $"Timer task threw: {error.Message}{Environment.NewLine}");
            }
            catch (VirtualMachineShutdownException)
            {
                // Expected while the emulator is closing.
            }
            finally
            {
                vm.RemoveThread(execution);
            }
        })
        {
            IsBackground = true,
            Name = "midlet:timer",
        };

        lock (_workers)
        {
            _workers.Add(worker);
        }

        worker.Start();
    }

    public void Cancel() => _cancellation.Cancel();
}
