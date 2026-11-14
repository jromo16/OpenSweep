package io.github.jromo16.opensweep.background

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import io.github.jromo16.opensweep.MainActivity
import io.github.jromo16.opensweep.R
import io.github.jromo16.opensweep.core.AppManager
import io.github.jromo16.opensweep.core.JunkType
import io.github.jromo16.opensweep.core.Permissions
import io.github.jromo16.opensweep.core.Prefs
import io.github.jromo16.opensweep.core.StorageScanner
import io.github.jromo16.opensweep.core.formatBytes
import java.util.concurrent.TimeUnit

/** Revisa la basura una vez al día y avisa si supera el umbral. */
class JunkWorker(ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {

    override suspend fun doWork(): Result {
        val ctx = applicationContext
        StorageWidget.updateAll(ctx)
        if (!Prefs.junkAlerts(ctx) || !Permissions.hasStorage(ctx)) return Result.success()
        val scanner = StorageScanner(ctx).apply { scan { } }
        val groups = scanner.junkGroups(AppManager.totalCacheBytes(ctx))
        val total = groups.filter { it.type.defaultOn && it.type != JunkType.EMPTY_FOLDERS }.sumOf { it.bytes }
        if (total >= THRESHOLD) notify(ctx, total)
        return Result.success()
    }

    companion object {
        private const val CHANNEL = "junk"
        private const val THRESHOLD = 300L * 1024 * 1024
        private const val WORK = "junk-check"

        fun schedule(ctx: Context) {
            val req = PeriodicWorkRequestBuilder<JunkWorker>(1, TimeUnit.DAYS)
                .setConstraints(Constraints.Builder().setRequiresBatteryNotLow(true).build())
                .build()
            WorkManager.getInstance(ctx).enqueueUniquePeriodicWork(WORK, ExistingPeriodicWorkPolicy.KEEP, req)
        }

        fun notify(ctx: Context, bytes: Long) {
            if (Build.VERSION.SDK_INT >= 33 &&
                ctx.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return
            val nm = ctx.getSystemService(NotificationManager::class.java)
            nm.createNotificationChannel(NotificationChannel(CHANNEL, "Avisos de basura", NotificationManager.IMPORTANCE_DEFAULT))
            val open = PendingIntent.getActivity(
                ctx, 1,
                Intent(ctx, MainActivity::class.java).putExtra(MainActivity.EXTRA_TAB, 1).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
            )
            val n = NotificationCompat.Builder(ctx, CHANNEL)
                .setSmallIcon(R.drawable.ic_stat_clean)
                .setContentTitle("Tienes ${formatBytes(bytes)} de basura")
                .setContentText("Toca para liberar espacio con OpenSweep.")
                .setContentIntent(open)
                .setAutoCancel(true)
                .build()
            NotificationManagerCompat.from(ctx).notify(1, n)
        }
    }
}
