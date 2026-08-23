package javax.microedition.lcdui;

public class Alert extends Screen {
    public static final int FOREVER = -2;
    public Alert(String title) {}
    public Alert(String title, String text, Image image, AlertType type) {}
    public void setString(String text) {}
    public String getString() { return null; }
    public void setTimeout(int timeout) {}
    public int getTimeout() { return 0; }
}
