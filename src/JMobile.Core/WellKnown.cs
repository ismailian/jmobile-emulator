namespace JMobile.Core;

/// <summary>
/// Internal names of the classes the virtual machine itself has to know about, either because
/// it allocates them (strings, class mirrors) or because it throws them.
/// </summary>
public static class WellKnown
{
    public const string Object = "java/lang/Object";
    public const string Class = "java/lang/Class";
    public const string String = "java/lang/String";
    public const string StringBuffer = "java/lang/StringBuffer";
    public const string Thread = "java/lang/Thread";
    public const string Runnable = "java/lang/Runnable";
    public const string System = "java/lang/System";

    public const string Throwable = "java/lang/Throwable";
    public const string Error = "java/lang/Error";
    public const string Exception = "java/lang/Exception";
    public const string RuntimeException = "java/lang/RuntimeException";

    public const string NullPointerException = "java/lang/NullPointerException";
    public const string ArithmeticException = "java/lang/ArithmeticException";
    public const string ClassCastException = "java/lang/ClassCastException";
    public const string ArrayIndexOutOfBoundsException = "java/lang/ArrayIndexOutOfBoundsException";
    public const string StringIndexOutOfBoundsException = "java/lang/StringIndexOutOfBoundsException";
    public const string IndexOutOfBoundsException = "java/lang/IndexOutOfBoundsException";
    public const string NegativeArraySizeException = "java/lang/NegativeArraySizeException";
    public const string ArrayStoreException = "java/lang/ArrayStoreException";
    public const string IllegalArgumentException = "java/lang/IllegalArgumentException";
    public const string IllegalStateException = "java/lang/IllegalStateException";
    public const string IllegalMonitorStateException = "java/lang/IllegalMonitorStateException";
    public const string InterruptedException = "java/lang/InterruptedException";
    public const string NumberFormatException = "java/lang/NumberFormatException";
    public const string UnsupportedOperationException = "java/lang/UnsupportedOperationException";
    public const string NoSuchElementException = "java/util/NoSuchElementException";

    public const string OutOfMemoryError = "java/lang/OutOfMemoryError";
    public const string StackOverflowError = "java/lang/StackOverflowError";
    public const string NoClassDefFoundError = "java/lang/NoClassDefFoundError";
    public const string ClassNotFoundException = "java/lang/ClassNotFoundException";
    public const string NoSuchMethodError = "java/lang/NoSuchMethodError";
    public const string NoSuchFieldError = "java/lang/NoSuchFieldError";
    public const string AbstractMethodError = "java/lang/AbstractMethodError";
    public const string IncompatibleClassChangeError = "java/lang/IncompatibleClassChangeError";
    public const string InternalError = "java/lang/InternalError";
    public const string VirtualMachineError = "java/lang/VirtualMachineError";

    public const string IOException = "java/io/IOException";
    public const string InputStream = "java/io/InputStream";
    public const string OutputStream = "java/io/OutputStream";

    /// <summary>Array class of bytes, needed whenever the host hands a buffer to guest code.</summary>
    public const string ByteArray = "[B";

    public const string CharArray = "[C";
    public const string IntArray = "[I";
    public const string ObjectArray = "[Ljava/lang/Object;";
    public const string StringArray = "[Ljava/lang/String;";
}
