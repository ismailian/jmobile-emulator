package javax.microedition.lcdui;

import javax.microedition.midlet.MIDlet;

public class Display {
    Display() {}
    public static Display getDisplay(MIDlet midlet) { return null; }
    public Displayable getCurrent() { return null; }
    public void setCurrent(Displayable next) {}
    public void setCurrent(Alert alert, Displayable next) {}
    public boolean isColor() { return true; }
    public int numColors() { return 0; }
    public int numAlphaLevels() { return 0; }
    public void callSerially(Runnable runnable) {}
    public boolean vibrate(int milliseconds) { return false; }
    public boolean flashBacklight(int milliseconds) { return false; }
}
