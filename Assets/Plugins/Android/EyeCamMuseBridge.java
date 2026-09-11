package com.eyecamxr;

import android.app.Activity;
import android.util.Log;

import com.choosemuse.libmuse.ConnectionState;
import com.choosemuse.libmuse.Eeg;
import com.choosemuse.libmuse.Muse;
import com.choosemuse.libmuse.MuseArtifactPacket;
import com.choosemuse.libmuse.MuseConnectionListener;
import com.choosemuse.libmuse.MuseConnectionPacket;
import com.choosemuse.libmuse.MuseDataListener;
import com.choosemuse.libmuse.MuseDataPacket;
import com.choosemuse.libmuse.MuseDataPacketType;
import com.choosemuse.libmuse.MuseListener;
import com.choosemuse.libmuse.MuseManagerAndroid;
import com.choosemuse.libmuse.MuseModel;
import com.choosemuse.libmuse.MusePreset;

import java.util.List;

/**
 * Poll-based libmuse bridge for EyeCamXR. No UnitySendMessage, no JSON on the
 * hot path: EEG samples accumulate in a synchronized ring; Unity calls
 * drain() once per frame and receives a packed double[]:
 *   [count, nCh, then per sample: museTimestampMicros, v0..v(nCh-1)]
 *
 * Presets: aux=false -> PRESET_21 (4ch) on classic Muses, PRESET_1021 on
 * Athena (MS_03); aux=true -> PRESET_20 (5ch) / PRESET_1022 (8ch). The
 * actual channel count is discovered from the first EEG packet's valuesSize,
 * which is authoritative over any preset assumption.
 */
public class EyeCamMuseBridge {
    private static final String TAG = "EyeCamMuse";
    private static final int RING = 4096;
    private static final int MAX_CH = 8;

    private final MuseManagerAndroid manager;
    private Muse muse;
    private volatile String status = "idle";
    private volatile int channelCount = 0;
    private volatile boolean wantAux = false;

    private final Object lock = new Object();
    private final double[][] ringVals = new double[RING][MAX_CH];
    private final double[] ringTs = new double[RING];
    private int head = 0;
    private int count = 0;

    private int blinkCount = 0;
    private int jawCount = 0;

    private static final Eeg[] EEG_MAP = {
        Eeg.EEG1, Eeg.EEG2, Eeg.EEG3, Eeg.EEG4,
        Eeg.AUX_LEFT, Eeg.AUX_RIGHT
    };

    public EyeCamMuseBridge(Activity activity) {
        manager = MuseManagerAndroid.getInstance();
        manager.setContext(activity);
        manager.setMuseListener(new MuseListener() {
            @Override
            public void museListChanged() {
                tryConnectFirst();
            }
        });
    }

    public void autoConnect(boolean aux) {
        wantAux = aux;
        status = "scanning";
        manager.stopListening();
        manager.startListening();
    }

    private void tryConnectFirst() {
        if (muse != null) return;
        List<Muse> muses = manager.getMuses();
        if (muses.isEmpty()) return;
        muse = muses.get(0);
        manager.stopListening();
        status = "connecting " + muse.getName();
        Log.i(TAG, "connecting to " + muse.getName());

        muse.unregisterAllListeners();
        muse.registerConnectionListener(new MuseConnectionListener() {
            @Override
            public void receiveMuseConnectionPacket(MuseConnectionPacket p, Muse m) {
                ConnectionState state = p.getCurrentConnectionState();
                status = state.toString().toLowerCase() + " " + m.getName();
                if (state == ConnectionState.CONNECTED) {
                    Log.i(TAG, "connected, model=" + m.getModel());
                }
                if (state == ConnectionState.DISCONNECTED) {
                    muse = null;
                    status = "scanning";
                    manager.startListening();
                }
            }
        });
        muse.registerDataListener(new MuseDataListener() {
            @Override
            public void receiveMuseDataPacket(MuseDataPacket p, Muse m) {
                if (p.packetType() != MuseDataPacketType.EEG) return;
                int n = (int) Math.min(p.valuesSize(), MAX_CH);
                if (channelCount == 0) {
                    channelCount = n;
                    Log.i(TAG, "EEG valuesSize=" + n);
                }
                synchronized (lock) {
                    int idx = (head + count) % RING;
                    if (count == RING) { head = (head + 1) % RING; count--; }
                    ringTs[idx] = p.timestamp();
                    for (int c = 0; c < n; c++) {
                        double v;
                        if (c < EEG_MAP.length) v = p.getEegChannelValue(EEG_MAP[c]);
                        else v = p.getValues().get(c);
                        ringVals[idx][c] = Double.isNaN(v) ? 0.0 : v;
                    }
                    for (int c = n; c < MAX_CH; c++) ringVals[idx][c] = 0.0;
                    count++;
                }
            }

            @Override
            public void receiveMuseArtifactPacket(MuseArtifactPacket p, Muse m) {
                if (p.getBlink()) blinkCount++;
                if (p.getJawClench()) jawCount++;
            }
        }, MuseDataPacketType.EEG);
        muse.registerDataListener(new MuseDataListener() {
            @Override
            public void receiveMuseDataPacket(MuseDataPacket p, Muse m) { }
            @Override
            public void receiveMuseArtifactPacket(MuseArtifactPacket p, Muse m) {
                if (p.getBlink()) blinkCount++;
                if (p.getJawClench()) jawCount++;
            }
        }, MuseDataPacketType.ARTIFACTS);

        try {
            MuseModel model = muse.getModel();
            MusePreset preset;
            if (model == MuseModel.MS_03) {
                preset = wantAux ? MusePreset.PRESET_1022 : MusePreset.PRESET_1021;
            } else {
                preset = wantAux ? MusePreset.PRESET_20 : MusePreset.PRESET_21;
            }
            muse.setPreset(preset);
            Log.i(TAG, "model=" + model + " preset=" + preset);
        } catch (Throwable t) {
            Log.w(TAG, "setPreset failed: " + t.getMessage());
        }
        muse.runAsynchronously();
    }

    /** Packed batch: [count, nCh, then per sample: tMicros, v0..v(nCh-1)]. */
    public double[] drain() {
        synchronized (lock) {
            int nCh = channelCount > 0 ? channelCount : 4;
            double[] out = new double[2 + count * (1 + nCh)];
            out[0] = count;
            out[1] = nCh;
            int o = 2;
            for (int i = 0; i < count; i++) {
                int idx = (head + i) % RING;
                out[o++] = ringTs[idx];
                for (int c = 0; c < nCh; c++) out[o++] = ringVals[idx][c];
            }
            head = 0;
            count = 0;
            return out;
        }
    }

    public String getStatus() { return status; }
    public int getChannelCount() { return channelCount; }
    public int getBlinkCount() { return blinkCount; }
    public int getJawCount() { return jawCount; }

    public void disconnect() {
        try {
            manager.stopListening();
            if (muse != null) {
                muse.unregisterAllListeners();
                muse.disconnect();
                muse = null;
            }
        } catch (Throwable t) {
            Log.w(TAG, "disconnect: " + t.getMessage());
        }
        status = "disconnected";
    }
}
