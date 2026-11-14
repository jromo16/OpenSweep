package io.github.jromo16.opensweep.background

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import io.github.jromo16.opensweep.MainActivity
import io.github.jromo16.opensweep.R
import io.github.jromo16.opensweep.core.DeviceInfo
import io.github.jromo16.opensweep.core.formatBytes

/** Widget de escritorio: espacio libre, RAM y botón «Limpiar». */
class StorageWidget : AppWidgetProvider() {

    override fun onUpdate(ctx: Context, manager: AppWidgetManager, ids: IntArray) {
        ids.forEach { manager.updateAppWidget(it, views(ctx)) }
    }

    companion object {
        fun updateAll(ctx: Context) {
            val manager = AppWidgetManager.getInstance(ctx)
            val ids = manager.getAppWidgetIds(ComponentName(ctx, StorageWidget::class.java))
            ids.forEach { manager.updateAppWidget(it, views(ctx)) }
        }

        private fun views(ctx: Context): RemoteViews {
            val st = DeviceInfo.storage()
            val mem = DeviceInfo.memory(ctx)
            val v = RemoteViews(ctx.packageName, R.layout.widget_storage)
            v.setTextViewText(R.id.widget_free, "${formatBytes(st.free)} libres")
            v.setTextViewText(R.id.widget_detail, "Almacenamiento ${(st.percent * 100).toInt()}% · RAM ${(mem.percent * 100).toInt()}%")
            v.setProgressBar(R.id.widget_bar, 100, (st.percent * 100).toInt(), false)
            val clean = PendingIntent.getActivity(
                ctx, 2,
                Intent(ctx, MainActivity::class.java).putExtra(MainActivity.EXTRA_TAB, 1).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
            )
            v.setOnClickPendingIntent(R.id.widget_clean, clean)
            v.setOnClickPendingIntent(R.id.widget_root, clean)
            return v
        }
    }
}
