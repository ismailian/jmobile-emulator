import java.util.Random;

import javax.microedition.lcdui.Canvas;
import javax.microedition.lcdui.Command;
import javax.microedition.lcdui.CommandListener;
import javax.microedition.lcdui.Display;
import javax.microedition.lcdui.Displayable;
import javax.microedition.lcdui.Font;
import javax.microedition.lcdui.Graphics;
import javax.microedition.midlet.MIDlet;
import javax.microedition.rms.RecordStore;

/**
 * A small but complete MIDlet, written the way a 2004 J2ME game was written.
 *
 * It exists to exercise the emulator end to end: a Canvas driven by its own render thread,
 * every Graphics primitive, game actions and raw key codes, a record store for the high score,
 * soft-key commands, and font measurement for the HUD.
 */
public class Dodger extends MIDlet implements CommandListener {

    private final GameCanvas canvas = new GameCanvas(this);
    private final Command exitCommand = new Command("Exit", Command.EXIT, 1);
    private final Command restartCommand = new Command("Restart", Command.SCREEN, 2);

    protected void startApp() {
        canvas.addCommand(restartCommand);
        canvas.addCommand(exitCommand);
        canvas.setCommandListener(this);

        Display.getDisplay(this).setCurrent(canvas);
        canvas.start();
    }

    protected void pauseApp() {
        canvas.stop();
    }

    protected void destroyApp(boolean unconditional) {
        canvas.stop();
        canvas.saveHighScore();
    }

    public void commandAction(Command command, Displayable displayable) {
        if (command == exitCommand) {
            destroyApp(true);
            notifyDestroyed();
        } else if (command == restartCommand) {
            canvas.restart();
        }
    }
}

/**
 * The playfield. Runs its own thread, which is how nearly every J2ME action game was built:
 * the thread advances the world at a fixed tick and asks for a repaint, and the system delivers
 * paint and key callbacks on its own event thread.
 */
class GameCanvas extends Canvas implements Runnable {

    private static final String STORE_NAME = "DodgerScores";
    private static final int ROCK_COUNT = 8;
    private static final int STAR_COUNT = 30;

    private final Dodger midlet;
    private final Random random = new Random(20040317L);

    private final int[] rockX = new int[ROCK_COUNT];
    private final int[] rockY = new int[ROCK_COUNT];
    private final int[] rockSize = new int[ROCK_COUNT];
    private final int[] rockSpeed = new int[ROCK_COUNT];

    private final int[] starX = new int[STAR_COUNT];
    private final int[] starY = new int[STAR_COUNT];
    private final int[] starSpeed = new int[STAR_COUNT];

    private Thread thread;
    private volatile boolean running;

    private int shipX;
    private int shipY;
    private int drift;
    private int score;
    private int highScore;
    private int lives = 3;
    private int invulnerable;
    private boolean gameOver;
    private long ticks;

    GameCanvas(Dodger midlet) {
        this.midlet = midlet;
        highScore = loadHighScore();
    }

    void start() {
        if (running) return;

        running = true;
        thread = new Thread(this);
        thread.start();
    }

    void stop() {
        running = false;

        Thread current = thread;
        thread = null;
        if (current == null) return;

        try {
            current.join(500);
        } catch (InterruptedException e) {
            // Shutting down anyway.
        }
    }

    void restart() {
        synchronized (this) {
            score = 0;
            lives = 3;
            gameOver = false;
            invulnerable = 40;
            ticks = 0;
            layOutField();
        }
        repaint();
    }

    // ---- the loop -------------------------------------------------------------------

    public void run() {
        layOutField();

        while (running) {
            long start = System.currentTimeMillis();

            synchronized (this) {
                advance();
            }

            repaint();

            // A fixed 25ms tick. Sleeping only for the remainder keeps the pace steady even
            // when a frame takes longer than usual.
            long elapsed = System.currentTimeMillis() - start;
            long remaining = 25 - elapsed;
            if (remaining > 0) {
                try {
                    Thread.sleep(remaining);
                } catch (InterruptedException e) {
                    return;
                }
            }
        }
    }

    private void layOutField() {
        int width = getWidth();
        int height = getHeight();

        shipX = width / 2;
        shipY = height - 22;

        for (int i = 0; i < STAR_COUNT; i++) {
            starX[i] = random.nextInt(Math.max(1, width));
            starY[i] = random.nextInt(Math.max(1, height));
            starSpeed[i] = 1 + random.nextInt(3);
        }

        for (int i = 0; i < ROCK_COUNT; i++) {
            resetRock(i, true);
        }
    }

    private void resetRock(int index, boolean anywhere) {
        int width = Math.max(1, getWidth());

        rockSize[index] = 5 + random.nextInt(8);
        rockX[index] = random.nextInt(width);
        // At start-up the field is seeded above the screen rather than across it, so the player
        // is not hit before the first frame is even visible.
        rockY[index] = anywhere
                ? -random.nextInt(Math.max(1, getHeight() * 2))
                : -rockSize[index] - random.nextInt(90);
        rockSpeed[index] = 1 + random.nextInt(3);
    }

    private void advance() {
        if (gameOver) return;

        ticks++;
        int width = getWidth();
        int height = getHeight();

        shipX += drift;
        if (shipX < 8) shipX = 8;
        if (shipX > width - 8) shipX = width - 8;

        for (int i = 0; i < STAR_COUNT; i++) {
            starY[i] += starSpeed[i];
            if (starY[i] > height) {
                starY[i] = 0;
                starX[i] = random.nextInt(Math.max(1, width));
            }
        }

        // The field speeds up as the score climbs, which is the whole difficulty curve.
        int bonus = (int) Math.min(3, ticks / 900);

        for (int i = 0; i < ROCK_COUNT; i++) {
            rockY[i] += rockSpeed[i] + bonus;

            if (rockY[i] - rockSize[i] > height) {
                resetRock(i, false);
                score += 10;
                continue;
            }

            if (invulnerable == 0 && hits(i)) {
                lives--;
                invulnerable = 60;

                if (lives <= 0) {
                    gameOver = true;
                    if (score > highScore) {
                        highScore = score;
                        saveHighScore();
                    }
                }
            }
        }

        if (invulnerable > 0) invulnerable--;
    }

    private boolean hits(int index) {
        int dx = rockX[index] - shipX;
        int dy = rockY[index] - shipY;
        int reach = rockSize[index] + 6;
        return (dx * dx) + (dy * dy) < reach * reach;
    }

    // ---- painting -------------------------------------------------------------------

    protected void paint(Graphics g) {
        int width = getWidth();
        int height = getHeight();

        synchronized (this) {
            g.setColor(0x060912);
            g.fillRect(0, 0, width, height);

            drawStars(g);
            drawRocks(g);
            if (!gameOver) drawShip(g);
            drawHud(g, width, height);

            if (gameOver) drawGameOver(g, width, height);
        }
    }

    private void drawStars(Graphics g) {
        for (int i = 0; i < STAR_COUNT; i++) {
            int shade = 0x303030 * starSpeed[i];
            g.setColor(shade > 0xFFFFFF ? 0xFFFFFF : shade);
            g.drawLine(starX[i], starY[i], starX[i], starY[i] + starSpeed[i]);
        }
    }

    private void drawRocks(Graphics g) {
        for (int i = 0; i < ROCK_COUNT; i++) {
            int size = rockSize[i];

            g.setColor(0x8A5A3C);
            g.fillArc(rockX[i] - size, rockY[i] - size, size * 2, size * 2, 0, 360);

            g.setColor(0xC08A5E);
            g.fillArc(rockX[i] - size + 2, rockY[i] - size + 2, size, size, 0, 360);

            g.setColor(0x4A2E1E);
            g.drawArc(rockX[i] - size, rockY[i] - size, size * 2, size * 2, 0, 360);
        }
    }

    private void drawShip(Graphics g) {
        // Flash while invulnerable so the player can see the grace period.
        if (invulnerable > 0 && (invulnerable / 4) % 2 == 0) return;

        g.setColor(0x3FB9C2);
        g.fillTriangle(shipX, shipY - 9, shipX - 7, shipY + 7, shipX + 7, shipY + 7);

        g.setColor(0xE6EAEF);
        g.fillTriangle(shipX, shipY - 5, shipX - 3, shipY + 3, shipX + 3, shipY + 3);

        g.setColor(0xDC9A3E);
        int flame = 3 + (int) (ticks % 4);
        g.fillTriangle(shipX - 3, shipY + 7, shipX + 3, shipY + 7, shipX, shipY + 7 + flame);
    }

    private void drawHud(Graphics g, int width, int height) {
        Font font = Font.getFont(Font.FACE_PROPORTIONAL, Font.STYLE_BOLD, Font.SIZE_SMALL);
        g.setFont(font);

        g.setColor(0x000000);
        g.fillRect(0, 0, width, font.getHeight() + 4);

        g.setColor(0xE6EAEF);
        g.drawString("Score " + score, 3, 2, Graphics.LEFT | Graphics.TOP);

        String best = "Best " + highScore;
        g.setColor(0x939EAB);
        g.drawString(best, width - 3, 2, Graphics.RIGHT | Graphics.TOP);

        for (int i = 0; i < lives; i++) {
            g.setColor(0x3FB9C2);
            g.fillTriangle(
                    width / 2 - 14 + (i * 11), font.getHeight(),
                    width / 2 - 18 + (i * 11), font.getHeight() + 6,
                    width / 2 - 10 + (i * 11), font.getHeight() + 6);
        }
    }

    private void drawGameOver(Graphics g, int width, int height) {
        Font font = Font.getFont(Font.FACE_PROPORTIONAL, Font.STYLE_BOLD, Font.SIZE_LARGE);
        g.setFont(font);

        int boxHeight = (font.getHeight() * 3) + 16;
        int boxTop = (height - boxHeight) / 2;

        g.setColor(0x11151B);
        g.fillRoundRect(10, boxTop, width - 20, boxHeight, 8, 8);
        g.setColor(0x3FB9C2);
        g.drawRoundRect(10, boxTop, width - 20, boxHeight, 8, 8);

        g.setColor(0xE6EAEF);
        g.drawString("Game Over", width / 2, boxTop + 6, Graphics.HCENTER | Graphics.TOP);

        Font small = Font.getFont(Font.FACE_PROPORTIONAL, Font.STYLE_PLAIN, Font.SIZE_SMALL);
        g.setFont(small);
        g.drawString("Score " + score, width / 2, boxTop + 8 + font.getHeight(),
                Graphics.HCENTER | Graphics.TOP);

        g.setColor(0x939EAB);
        g.drawString("Press 5 to play again", width / 2, boxTop + 8 + font.getHeight() + small.getHeight(),
                Graphics.HCENTER | Graphics.TOP);
    }

    // ---- input ----------------------------------------------------------------------

    protected void keyPressed(int keyCode) {
        int action = getGameAction(keyCode);

        if (gameOver && (action == FIRE || keyCode == KEY_NUM5)) {
            restart();
            return;
        }

        if (action == LEFT || keyCode == KEY_NUM4) drift = -4;
        else if (action == RIGHT || keyCode == KEY_NUM6) drift = 4;
    }

    protected void keyReleased(int keyCode) {
        int action = getGameAction(keyCode);

        if (action == LEFT || keyCode == KEY_NUM4 || action == RIGHT || keyCode == KEY_NUM6) {
            drift = 0;
        }
    }

    protected void pointerPressed(int x, int y) {
        if (gameOver) {
            restart();
            return;
        }

        drift = x < getWidth() / 2 ? -4 : 4;
    }

    protected void pointerReleased(int x, int y) {
        drift = 0;
    }

    protected void hideNotify() {
        drift = 0;
    }

    // ---- persistence ----------------------------------------------------------------

    private int loadHighScore() {
        RecordStore store = null;
        try {
            store = RecordStore.openRecordStore(STORE_NAME, true);
            if (store.getNumRecords() == 0) return 0;

            byte[] record = store.getRecord(1);
            if (record == null || record.length < 4) return 0;

            return ((record[0] & 0xFF) << 24)
                    | ((record[1] & 0xFF) << 16)
                    | ((record[2] & 0xFF) << 8)
                    | (record[3] & 0xFF);
        } catch (Exception e) {
            return 0;
        } finally {
            closeQuietly(store);
        }
    }

    void saveHighScore() {
        RecordStore store = null;
        try {
            store = RecordStore.openRecordStore(STORE_NAME, true);

            byte[] record = new byte[] {
                    (byte) (highScore >> 24),
                    (byte) (highScore >> 16),
                    (byte) (highScore >> 8),
                    (byte) highScore,
            };

            if (store.getNumRecords() == 0) store.addRecord(record, 0, record.length);
            else store.setRecord(1, record, 0, record.length);
        } catch (Exception e) {
            // A save that fails is not worth interrupting the game over.
        } finally {
            closeQuietly(store);
        }
    }

    private static void closeQuietly(RecordStore store) {
        if (store == null) return;

        try {
            store.closeRecordStore();
        } catch (Exception e) {
            // Nothing useful to do.
        }
    }
}
