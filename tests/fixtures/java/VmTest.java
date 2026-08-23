// Conformance fixture for the JMobile interpreter.
//
// Compiled once with `javac --release 8` and checked in as .class files so the test suite runs
// without a JDK present. Every method here is called from JMobile.Tests and its result compared
// against the value a real JVM produces.

import java.util.Enumeration;
import java.util.Hashtable;
import java.util.Random;
import java.util.Vector;

public class VmTest {

    // ---- integer and long arithmetic, including the edge cases the JVM specifies ----

    public static int arithmetic(int a, int b) {
        int r = a + b;
        r = r - (a * 3);
        r = r ^ (a | b);
        r = r & 0x7fffffff;
        r = r << 3;
        r = r >> 2;
        r = r >>> 1;
        if (b != 0) {
            r += a / b;
            r += a % b;
        }
        return r;
    }

    public static int divideEdgeCases() {
        int min = Integer.MIN_VALUE;
        int a = min / -1;
        int b = min % -1;
        return a + b;
    }

    public static long longEdgeCases() {
        long min = Long.MIN_VALUE;
        return (min / -1L) + (min % -1L);
    }

    public static long longArithmetic(long a, long b) {
        long r = a * b;
        r += a << 5;
        r -= b >> 3;
        r ^= (a >>> 7);
        return r;
    }

    public static int longCompare(long a, long b) {
        if (a < b) return -1;
        if (a > b) return 1;
        return 0;
    }

    // ---- floating point ----

    public static double doubleMath(double x) {
        return Math.sqrt(x) + Math.abs(-x) + Math.floor(x / 3.0) + Math.ceil(x / 7.0);
    }

    public static int floatToIntEdge() {
        float nan = Float.intBitsToFloat(0x7fc00000);
        float big = 1e20f;
        float small = -1e20f;
        return (int) nan + (int) big + (int) small;
    }

    public static int floatCompare(float a, float b) {
        if (a < b) return -1;
        if (a > b) return 1;
        if (a == b) return 0;
        return 2; // unordered
    }

    // ---- arrays ----

    public static int arraySum(int[] values) {
        int total = 0;
        for (int i = 0; i < values.length; i++) total += values[i];
        return total;
    }

    public static int multiArray(int rows, int cols) {
        int[][] grid = new int[rows][cols];
        for (int r = 0; r < rows; r++) {
            for (int c = 0; c < cols; c++) grid[r][c] = (r * cols) + c;
        }
        int total = 0;
        for (int r = 0; r < rows; r++) total += grid[r][cols - 1];
        return total;
    }

    public static int arrayTypes() {
        byte[] bytes = new byte[4];
        char[] chars = new char[4];
        short[] shorts = new short[4];
        boolean[] flags = new boolean[4];
        long[] longs = new long[2];

        bytes[0] = (byte) 200;      // wraps to -56
        chars[0] = (char) 65535;
        shorts[0] = (short) 70000;  // wraps to 4464
        flags[0] = true;
        longs[0] = 0x1122334455667788L;

        return bytes[0] + chars[0] + shorts[0] + (flags[0] ? 1 : 0) + (int) longs[0];
    }

    // ---- strings ----

    public static String buildString(int n) {
        StringBuffer sb = new StringBuffer();
        for (int i = 0; i < n; i++) {
            sb.append(i).append(':').append(i % 2 == 0 ? "even" : "odd").append(';');
        }
        return sb.toString();
    }

    public static int stringOps(String s) {
        int r = s.length();
        r += s.indexOf('a');
        r += s.hashCode();
        r += s.substring(1).length();
        r += s.toUpperCase().charAt(0);
        r += s.trim().length();
        r += s.replace('a', 'b').indexOf('b');
        return r;
    }

    public static String concat(String a, int b, char c, boolean d) {
        return a + b + c + d;
    }

    // ---- control flow ----

    public static int tableSwitch(int n) {
        switch (n) {
            case 0: return 10;
            case 1: return 20;
            case 2: return 30;
            case 3: return 40;
            case 4: return 50;
            default: return -1;
        }
    }

    public static int lookupSwitch(int n) {
        switch (n) {
            case 1: return 100;
            case 100: return 200;
            case 10000: return 300;
            case 1000000: return 400;
            default: return -1;
        }
    }

    public static int loops(int n) {
        int total = 0;
        for (int i = 0; i < n; i++) {
            if (i % 3 == 0) continue;
            if (i > n - 3) break;
            total += i;
        }
        int j = 0;
        while (j < n) { total += j; j += 2; }
        do { total++; } while (total < 0);
        return total;
    }

    public static int recursion(int n) {
        if (n <= 1) return 1;
        return n * recursion(n - 1);
    }

    // ---- exceptions ----

    public static int exceptions(int mode) {
        int result = 0;
        try {
            if (mode == 0) throw new IllegalStateException("zero");
            if (mode == 1) { int[] a = new int[1]; result = a[5]; }
            if (mode == 2) { Object o = null; result = o.hashCode(); }
            if (mode == 3) { result = 10 / (mode - 3); }
            if (mode == 4) { Object o = "text"; result = ((Integer) o).intValue(); }
            result += 1000;
        } catch (NullPointerException e) {
            result = 2;
        } catch (ArrayIndexOutOfBoundsException e) {
            result = 1;
        } catch (ArithmeticException e) {
            result = 3;
        } catch (ClassCastException e) {
            result = 4;
        } catch (RuntimeException e) {
            result = 0;
        } finally {
            result += 100;
        }
        return result;
    }

    public static int nestedFinally(int n) {
        int r = 0;
        try {
            try {
                if (n == 0) return r + 1;
                r += 2;
            } finally {
                r += 10;
            }
            r += 100;
        } finally {
            r += 1000;
        }
        return r;
    }

    // ---- polymorphism ----

    interface Shape { int area(); }

    static class Rect implements Shape {
        int w, h;
        Rect(int w, int h) { this.w = w; this.h = h; }
        public int area() { return w * h; }
        public String toString() { return "Rect"; }
    }

    static class Square extends Rect {
        Square(int s) { super(s, s); }
        public int area() { return w * w; }
        public String toString() { return "Square"; }
    }

    public static int virtualDispatch() {
        Rect r = new Rect(3, 4);
        Rect s = new Square(5);
        return r.area() + s.area();
    }

    public static int interfaceDispatch() {
        Shape[] shapes = new Shape[] { new Rect(2, 3), new Square(4) };
        int total = 0;
        for (int i = 0; i < shapes.length; i++) total += shapes[i].area();
        return total;
    }

    public static String toStringDispatch() {
        Object o = new Square(2);
        return o.toString();
    }

    public static int instanceChecks() {
        Object o = new Square(2);
        int r = 0;
        if (o instanceof Square) r += 1;
        if (o instanceof Rect) r += 2;
        if (o instanceof Shape) r += 4;
        if (o instanceof String) r += 8;
        if (!(o instanceof Object)) r += 16;
        return r;
    }

    // ---- static state ----

    static int counter = 5;
    static final int CONSTANT = 42;
    static String label;

    static { label = "initialised"; counter += 1; }

    public static int staticState() {
        counter *= 2;
        return counter + CONSTANT + label.length();
    }

    // ---- collections ----

    public static int vectorTest() {
        Vector v = new Vector();
        for (int i = 0; i < 10; i++) v.addElement(new Integer(i));
        v.removeElementAt(0);
        v.insertElementAt(new Integer(99), 0);

        int total = 0;
        for (Enumeration e = v.elements(); e.hasMoreElements();) {
            total += ((Integer) e.nextElement()).intValue();
        }
        return total + v.size() + v.indexOf(new Integer(5));
    }

    public static int hashtableTest() {
        Hashtable h = new Hashtable();
        for (int i = 0; i < 20; i++) h.put("key" + i, new Integer(i * i));

        int total = 0;
        for (int i = 0; i < 20; i++) total += ((Integer) h.get("key" + i)).intValue();

        h.remove("key3");
        return total + h.size() + (h.containsKey("key3") ? 1000 : 0);
    }

    public static long randomTest(long seed) {
        Random r = new Random(seed);
        long total = 0;
        for (int i = 0; i < 10; i++) total += r.nextInt(1000);
        total += r.nextLong() & 0xffffL;
        total += r.nextBoolean() ? 1 : 0;
        return total;
    }

    // ---- threads and synchronisation ----

    static int shared = 0;

    public static int threadTest() throws Exception {
        Runnable job = new Runnable() {
            public void run() {
                for (int i = 0; i < 1000; i++) {
                    synchronized (VmTest.class) { shared++; }
                }
            }
        };

        Thread a = new Thread(job);
        Thread b = new Thread(job);
        a.start();
        b.start();
        a.join();
        b.join();
        return shared;
    }

    // ---- regressions ----------------------------------------------------------------

    // Obfuscators name classes with a single letter. A loader that mistakes a one-character
    // class name for a primitive type descriptor cannot instantiate any of them.
    static class e {
        int v;
        e(int v) { this.v = v; }
        int get() { return v; }
    }

    public static int singleLetterClass() {
        e instance = new e(41);
        e[] array = new e[2];
        array[0] = instance;
        array[1] = new e(1);
        return array[0].get() + array[1].get();
    }

    // A class initializer runs on the calling thread, in the middle of evaluating an
    // expression, with operands already on the stack. If the nested frames are placed using a
    // stale stack pointer they overwrite those operands.
    static class Late {
        static final String TAG;
        static int counter;
        static { counter = compute(); TAG = "late" + counter; }
        static int compute() {
            int total = 0;
            for (int i = 0; i < 10; i++) total += widen(i);
            return total;
        }
        static int widen(int i) { return i; }
    }

    private static String join(String a, String b, String c) {
        return a + "|" + b + "|" + c;
    }

    public static String clinitMidExpression() {
        // "head" is on the operand stack when the getstatic below triggers Late's initializer.
        return join("head", Late.TAG, "tail");
    }

    // Fields are identified by name *and* descriptor, so an obfuscated class may declare
    // several fields sharing a name.
    static class Overloaded {
        static int a;
        static String a2;
        static long a3;
        static { a = 7; a2 = "seven"; a3 = 70L; }
    }

    public static String overloadedFieldNames() {
        return Overloaded.a + ":" + Overloaded.a2 + ":" + Overloaded.a3;
    }

    // A subclass of a runtime-library class must keep the representation the base class needs.
    static class CountingThread extends Thread {
        static int runs;
        public void run() { runs++; }
    }

    public static int subclassOfRuntimeClass() throws Exception {
        CountingThread t = new CountingThread();
        t.start();
        t.join();
        return CountingThread.runs;
    }

    // ---- entry point for a quick manual run ----

    public static void main(String[] args) {
        System.out.println("arithmetic=" + arithmetic(17, 5));
        System.out.println("strings=" + stringOps("a banana"));
        System.out.println("vector=" + vectorTest());
        System.out.println("hashtable=" + hashtableTest());
    }
}
