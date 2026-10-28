package com.scrun.sensor;

import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;

// 가속도 센서를 가장 빠른 속도로 읽어 링 버퍼에 쌓는다. C#(TreadmillSensor)이 매 프레임 drain()으로 가져간다.
// 콜백은 별도 스레드에서 받는다(메인 스레드가 바빠도 샘플이 밀리지 않게).
// 안드로이드 12+는 HIGH_SAMPLING_RATE_SENSORS 권한이 없으면 200Hz로 제한된다(권한은 SpeedLabManifest가 넣음).
public class ScrunAccel implements SensorEventListener {
    private static ScrunAccel instance;

    private static final int CAP = 1 << 15;
    private final long[] times = new long[CAP];
    private final float[] values = new float[CAP * 3];
    private int head, count;
    private long dropped;

    private final SensorManager manager;
    private final Sensor sensor;
    private final HandlerThread thread;
    private final long startNs;

    private ScrunAccel(Context ctx) {
        manager = (SensorManager) ctx.getSystemService(Context.SENSOR_SERVICE);
        sensor = manager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
        thread = new HandlerThread("ScrunAccel");
        thread.start();
        startNs = SystemClock.elapsedRealtimeNanos();
        if (sensor != null)
            manager.registerListener(this, sensor, 0, new Handler(thread.getLooper()));
    }

    public static synchronized boolean start(Context ctx) {
        if (instance == null) instance = new ScrunAccel(ctx);
        return instance.sensor != null;
    }

    public static synchronized void stop() {
        if (instance == null) return;
        instance.manager.unregisterListener(instance);
        instance.thread.quitSafely();
        instance = null;
    }

    public static String info() {
        ScrunAccel a = instance;
        if (a == null || a.sensor == null) return "no sensor";
        Sensor s = a.sensor;
        return s.getName() + " / " + s.getVendor() + " / minDelay " + s.getMinDelay() + "us / range " + s.getMaximumRange();
    }

    public static long dropped() {
        ScrunAccel a = instance;
        return a == null ? 0 : a.dropped;
    }

    // [t(초, 시작 기준), x, y, z] × n
    public static double[] drain() {
        ScrunAccel a = instance;
        if (a == null) return new double[0];
        synchronized (a) {
            double[] out = new double[a.count * 4];
            int start = (a.head - a.count + CAP) & (CAP - 1);
            for (int i = 0; i < a.count; i++) {
                int k = (start + i) & (CAP - 1);
                out[i * 4] = (a.times[k] - a.startNs) * 1e-9;
                out[i * 4 + 1] = a.values[k * 3];
                out[i * 4 + 2] = a.values[k * 3 + 1];
                out[i * 4 + 3] = a.values[k * 3 + 2];
            }
            a.count = 0;
            return out;
        }
    }

    @Override
    public void onSensorChanged(SensorEvent e) {
        synchronized (this) {
            times[head] = e.timestamp;
            values[head * 3] = e.values[0];
            values[head * 3 + 1] = e.values[1];
            values[head * 3 + 2] = e.values[2];
            head = (head + 1) & (CAP - 1);
            if (count < CAP) count++;
            else dropped++;
        }
    }

    @Override
    public void onAccuracyChanged(Sensor s, int accuracy) { }
}
