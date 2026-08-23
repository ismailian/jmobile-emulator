package javax.microedition.lcdui;

public abstract class Displayable {
    Displayable() {}
    public int getWidth() { return 0; }
    public int getHeight() { return 0; }
    public boolean isShown() { return false; }
    public String getTitle() { return null; }
    public void setTitle(String title) {}
    public void addCommand(Command command) {}
    public void removeCommand(Command command) {}
    public void setCommandListener(CommandListener listener) {}
    public void setTicker(Ticker ticker) {}
}
