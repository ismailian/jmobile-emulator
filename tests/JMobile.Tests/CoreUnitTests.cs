using JMobile.Core;
using JMobile.Core.ClassFile;
using JMobile.Core.Execution;
using JMobile.Core.Loading;
using JMobile.Core.Model;
using JMobile.Runtime;
using JMobile.Runtime.Lang;

namespace JMobile.Tests;

/// <summary>Unit tests for the pieces of the virtual machine that can be exercised directly.</summary>
public static class CoreUnitTests
{
    public static void Register()
    {
        RegisterDescriptorTests();
        RegisterSwitchTests();
        RegisterTypeSystemTests();
        RegisterTextTests();
    }

    private static void RegisterDescriptorTests()
    {
        Test.Suite("descriptors");

        Test.Case("parameter slots count long and double as two", () =>
        {
            Test.Equal(0, Descriptor.ParameterSlots("()V"));
            Test.Equal(1, Descriptor.ParameterSlots("(I)V"));
            Test.Equal(2, Descriptor.ParameterSlots("(J)V"));
            Test.Equal(4, Descriptor.ParameterSlots("(JD)V"), "two 64-bit parameters");
            Test.Equal(3, Descriptor.ParameterSlots("(ILjava/lang/String;J)V") - 1,
                "int + reference + long is four slots");
        });

        Test.Case("parameter slot offsets address by parameter, not slot", () =>
        {
            int[] offsets = Descriptor.ParameterSlotOffsets("(IJLjava/lang/String;D)V");
            Test.Equal(4, offsets.Length);
            Test.Equal(0, offsets[0], "int at slot 0");
            Test.Equal(1, offsets[1], "long at slot 1");
            Test.Equal(3, offsets[2], "reference after the long's two slots");
            Test.Equal(4, offsets[3], "double after the reference");
        });

        Test.Case("return kinds", () =>
        {
            Test.Equal(ValueKind.Void, Descriptor.ReturnKind("()V"));
            Test.Equal(ValueKind.Int, Descriptor.ReturnKind("()Z"));
            Test.Equal(ValueKind.Long, Descriptor.ReturnKind("(II)J"));
            Test.Equal(ValueKind.Reference, Descriptor.ReturnKind("()[Ljava/lang/String;"));
        });

        Test.Case("array descriptors render readably", () =>
        {
            Test.Equal("int[][]", Descriptor.ToDisplayName("[[I"));
            Test.Equal("java.lang.String[]", Descriptor.ToDisplayName("[Ljava/lang/String;"));
            Test.Equal("com.example.Game", Descriptor.ToDisplayName("com/example/Game"));
        });

        Test.Case("parameter types split correctly", () =>
        {
            string[] types = Descriptor.ParameterTypes("(I[[BLjava/lang/String;D)V");
            Test.Equal(4, types.Length);
            Test.Equal("I", types[0]);
            Test.Equal("[[B", types[1]);
            Test.Equal("Ljava/lang/String;", types[2]);
            Test.Equal("D", types[3]);
        });
    }

    private static void RegisterSwitchTests()
    {
        Test.Suite("switch tables");

        Test.Case("dense switch indexes directly", () =>
        {
            var table = new SwitchTable
            {
                DefaultTarget = 99,
                Low = 10,
                High = 13,
                Targets = [1, 2, 3, 4],
            };

            Test.Equal(1, table.SelectDense(10));
            Test.Equal(4, table.SelectDense(13));
            Test.Equal(99, table.SelectDense(9), "below the range");
            Test.Equal(99, table.SelectDense(14), "above the range");
        });

        Test.Case("sparse switch binary searches", () =>
        {
            var table = new SwitchTable
            {
                DefaultTarget = 0,
                Keys = [-100, 1, 50, 1000, 1000000],
                Targets = [11, 22, 33, 44, 55],
            };

            Test.Equal(11, table.SelectSparse(-100), "first key");
            Test.Equal(33, table.SelectSparse(50), "middle key");
            Test.Equal(55, table.SelectSparse(1000000), "last key");
            Test.Equal(0, table.SelectSparse(7), "missing key falls to default");
        });
    }

    private static void RegisterTypeSystemTests()
    {
        Test.Suite("type system");

        VirtualMachine vm = NewHeadlessVm();

        Test.Case("everything is assignable to Object", () =>
        {
            JavaClass objectClass = vm.LoadClass(WellKnown.Object);
            Test.True(objectClass.IsAssignableFrom(vm.LoadClass(WellKnown.String)), "String");
            Test.True(objectClass.IsAssignableFrom(vm.LoadClass("[I")), "int[]");
            Test.True(objectClass.IsAssignableFrom(vm.LoadClass(WellKnown.Runnable)), "an interface");
        });

        Test.Case("subclass tests use the depth chain", () =>
        {
            JavaClass throwable = vm.LoadClass(WellKnown.Throwable);
            JavaClass runtime = vm.LoadClass(WellKnown.RuntimeException);
            JavaClass npe = vm.LoadClass(WellKnown.NullPointerException);

            Test.True(throwable.IsAssignableFrom(npe), "Throwable from NullPointerException");
            Test.True(runtime.IsAssignableFrom(npe), "RuntimeException from NullPointerException");
            Test.False(npe.IsAssignableFrom(throwable), "not the other way round");
        });

        Test.Case("array covariance follows the element type", () =>
        {
            JavaClass objectArray = vm.LoadClass("[Ljava/lang/Object;");
            JavaClass stringArray = vm.LoadClass("[Ljava/lang/String;");
            JavaClass intArray = vm.LoadClass("[I");
            JavaClass longArray = vm.LoadClass("[J");

            Test.True(objectArray.IsAssignableFrom(stringArray), "Object[] from String[]");
            Test.False(stringArray.IsAssignableFrom(objectArray), "not the other way round");
            Test.False(intArray.IsAssignableFrom(longArray), "primitive arrays are invariant");
            Test.False(objectArray.IsAssignableFrom(intArray), "int[] is not an Object[]");
        });

        Test.Case("interfaces resolve transitively", () =>
        {
            JavaClass runnable = vm.LoadClass(WellKnown.Runnable);
            JavaClass thread = vm.LoadClass(WellKnown.Thread);
            Test.True(runnable.IsAssignableFrom(thread), "Thread implements Runnable");
        });

        Test.Case("class mirrors are stable", () =>
        {
            JavaClass type = vm.LoadClass(WellKnown.String);
            Test.True(ReferenceEquals(vm.GetClassMirror(type), vm.GetClassMirror(type)),
                "getClass() must return the same object each time");
        });

        Test.Case("interned strings are canonical", () =>
        {
            Test.True(ReferenceEquals(vm.Intern("shared"), vm.Intern("shared")), "same literal");
            Test.False(ReferenceEquals(vm.Intern("shared"), vm.NewString("shared")),
                "a fresh string is a different object");
        });

        Test.Case("native library methods are reachable", () =>
        {
            JavaThread thread = vm.CreateThread("unit");
            JavaClass math = vm.LoadClass("java/lang/Math");

            JavaMethod abs = math.FindMethod("abs", "(I)I")!;
            Test.Equal(7, vm.CallStatic(thread, abs, JavaValue.Int(-7)).AsInt);

            JavaMethod sqrt = math.FindMethod("sqrt", "(D)D")!;
            Test.Equal(4.0, vm.CallStatic(thread, sqrt, JavaValue.Double(16.0)).AsDouble);
        });

        Test.Case("arrays report their element type", () =>
        {
            Test.Equal(FieldType.Int, vm.LoadClass("[I").ArrayElementType);
            Test.Equal(FieldType.Byte, vm.LoadClass("[B").ArrayElementType);
            Test.Equal(FieldType.Reference, vm.LoadClass("[Ljava/lang/String;").ArrayElementType);
        });

        Test.Case("a missing class is reported, not thrown from the loader", () =>
            Test.Null(vm.TryLoadClass("com/example/Missing"), "TryLoadClass returns null"));
    }

    private static void RegisterTextTests()
    {
        Test.Suite("java text");

        Test.Case("string hash matches Java", () =>
        {
            Test.Equal(0, JavaText.StringHashCode(""));
            Test.Equal(97, JavaText.StringHashCode("a"));
            Test.Equal(96354, JavaText.StringHashCode("abc"));
            Test.Equal(-2080325883, JavaText.StringHashCode("javax.microedition.lcdui"));
        });

        Test.Case("double formatting matches Java", () =>
        {
            Test.Equal("1.0", JavaText.DoubleToString(1.0));
            Test.Equal("0.0", JavaText.DoubleToString(0.0));
            Test.Equal("-0.0", JavaText.DoubleToString(-0.0));
            Test.Equal("NaN", JavaText.DoubleToString(double.NaN));
            Test.Equal("Infinity", JavaText.DoubleToString(double.PositiveInfinity));
            Test.Equal("1.5", JavaText.DoubleToString(1.5));
        });

        Test.Case("float formatting keeps a decimal point", () =>
        {
            Test.Equal("2.0", JavaText.FloatToString(2.0f));
            Test.Equal("-3.25", JavaText.FloatToString(-3.25f));
        });

        Test.Case("radix conversion matches Java", () =>
        {
            Test.Equal("ff", JavaText.IntToString(255, 16));
            Test.Equal("-ff", JavaText.IntToString(-255, 16));
            Test.Equal("1111", JavaText.IntToString(15, 2));
            Test.Equal("-2147483648", JavaText.IntToString(int.MinValue, 10));
            Test.Equal("-80000000", JavaText.IntToString(int.MinValue, 16),
                "MIN_VALUE must not overflow while being negated");
        });

        Test.Case("integer parsing rejects overflow", () =>
        {
            Test.True(JavaText.TryParseInt("2147483647", 10, out int max), "MAX_VALUE parses");
            Test.Equal(int.MaxValue, max);

            Test.True(JavaText.TryParseInt("-2147483648", 10, out int min), "MIN_VALUE parses");
            Test.Equal(int.MinValue, min);

            Test.False(JavaText.TryParseInt("2147483648", 10, out _), "one past MAX_VALUE fails");
            Test.False(JavaText.TryParseInt("", 10, out _), "empty fails");
            Test.False(JavaText.TryParseInt("12x", 10, out _), "trailing garbage fails");
            Test.True(JavaText.TryParseInt("7f", 16, out int hex), "hex parses");
            Test.Equal(127, hex);
        });

        Test.Case("long parsing handles the extremes", () =>
        {
            Test.True(JavaText.TryParseLong("-9223372036854775808", 10, out long min), "MIN_VALUE");
            Test.Equal(long.MinValue, min);
            Test.False(JavaText.TryParseLong("9223372036854775808", 10, out _), "one past MAX_VALUE");
        });
    }

    /// <summary>Builds a VM with the headless subset of the library and no class source.</summary>
    internal static VirtualMachine NewHeadlessVm() => new(new VirtualMachineOptions
    {
        ClassSource = EmptyClassSource.Instance,
        Modules = RuntimeLibrary.Headless,
    });
}
