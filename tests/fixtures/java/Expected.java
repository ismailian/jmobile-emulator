// Produces the reference values the JMobile test suite compares against.
//
// Run on a real JVM, its output is checked in as expected.txt. Every line is one assertion:
// the emulator must produce the same value for the same call.

public class Expected {

    private static final StringBuilder OUT = new StringBuilder();

    static void emit(String name, String value) {
        OUT.append(name).append('=').append(value).append('\n');
    }

    public static void main(String[] args) throws Exception {
        emit("arithmetic.17.5", Integer.toString(VmTest.arithmetic(17, 5)));
        emit("arithmetic.-31.7", Integer.toString(VmTest.arithmetic(-31, 7)));
        emit("arithmetic.0.0", Integer.toString(VmTest.arithmetic(0, 0)));
        emit("arithmetic.max.min",
                Integer.toString(VmTest.arithmetic(Integer.MAX_VALUE, Integer.MIN_VALUE)));

        emit("divideEdgeCases", Integer.toString(VmTest.divideEdgeCases()));
        emit("longEdgeCases", Long.toString(VmTest.longEdgeCases()));
        emit("longArithmetic", Long.toString(VmTest.longArithmetic(0x0123456789abcdefL, -98765432101L)));
        emit("longCompare.lt", Integer.toString(VmTest.longCompare(-5L, 900000000000L)));
        emit("longCompare.gt", Integer.toString(VmTest.longCompare(900000000000L, -5L)));
        emit("longCompare.eq", Integer.toString(VmTest.longCompare(42L, 42L)));

        emit("doubleMath", Double.toString(VmTest.doubleMath(1234.5)));
        emit("floatToIntEdge", Integer.toString(VmTest.floatToIntEdge()));
        emit("floatCompare.nan", Integer.toString(
                VmTest.floatCompare(Float.intBitsToFloat(0x7fc00000), 1.0f)));
        emit("floatCompare.lt", Integer.toString(VmTest.floatCompare(1.5f, 2.5f)));

        emit("arraySum", Integer.toString(VmTest.arraySum(new int[] { 5, -3, 17, 0, 99 })));
        emit("multiArray", Integer.toString(VmTest.multiArray(7, 5)));
        emit("arrayTypes", Integer.toString(VmTest.arrayTypes()));

        emit("buildString", VmTest.buildString(6));
        emit("stringOps", Integer.toString(VmTest.stringOps("a banana")));
        emit("stringHash", Integer.toString("javax.microedition.lcdui".hashCode()));
        emit("concat", VmTest.concat("v", -7, 'z', true));

        emit("tableSwitch.3", Integer.toString(VmTest.tableSwitch(3)));
        emit("tableSwitch.9", Integer.toString(VmTest.tableSwitch(9)));
        emit("lookupSwitch.10000", Integer.toString(VmTest.lookupSwitch(10000)));
        emit("lookupSwitch.7", Integer.toString(VmTest.lookupSwitch(7)));
        emit("loops", Integer.toString(VmTest.loops(20)));
        emit("recursion", Integer.toString(VmTest.recursion(12)));

        emit("exceptions.0", Integer.toString(VmTest.exceptions(0)));
        emit("exceptions.1", Integer.toString(VmTest.exceptions(1)));
        emit("exceptions.2", Integer.toString(VmTest.exceptions(2)));
        emit("exceptions.3", Integer.toString(VmTest.exceptions(3)));
        emit("exceptions.4", Integer.toString(VmTest.exceptions(4)));
        emit("exceptions.9", Integer.toString(VmTest.exceptions(9)));
        emit("nestedFinally.0", Integer.toString(VmTest.nestedFinally(0)));
        emit("nestedFinally.1", Integer.toString(VmTest.nestedFinally(1)));

        emit("virtualDispatch", Integer.toString(VmTest.virtualDispatch()));
        emit("interfaceDispatch", Integer.toString(VmTest.interfaceDispatch()));
        emit("toStringDispatch", VmTest.toStringDispatch());
        emit("instanceChecks", Integer.toString(VmTest.instanceChecks()));

        emit("staticState", Integer.toString(VmTest.staticState()));

        emit("vectorTest", Integer.toString(VmTest.vectorTest()));
        emit("hashtableTest", Integer.toString(VmTest.hashtableTest()));
        emit("randomTest.12345", Long.toString(VmTest.randomTest(12345L)));
        emit("randomTest.0", Long.toString(VmTest.randomTest(0L)));

        emit("singleLetterClass", Integer.toString(VmTest.singleLetterClass()));
        emit("clinitMidExpression", VmTest.clinitMidExpression());
        emit("overloadedFieldNames", VmTest.overloadedFieldNames());
        emit("subclassOfRuntimeClass", Integer.toString(VmTest.subclassOfRuntimeClass()));

        emit("threadTest", Integer.toString(VmTest.threadTest()));

        System.out.print(OUT.toString());
    }
}
