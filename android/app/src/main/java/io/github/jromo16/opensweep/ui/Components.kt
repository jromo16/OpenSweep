package io.github.jromo16.opensweep.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jromo16.opensweep.ui.theme.Brand
import io.github.jromo16.opensweep.ui.theme.Danger
import io.github.jromo16.opensweep.ui.theme.Warn

@Composable
fun SectionCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh),
    ) {
        Column(Modifier.padding(16.dp), content = content)
    }
}

@Composable
fun ScreenTitle(title: String, subtitle: String? = null) {
    Column(Modifier.padding(bottom = 12.dp)) {
        Text(title, fontSize = 26.sp, fontWeight = FontWeight.SemiBold)
        if (subtitle != null) Text(subtitle, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
    }
}

fun usageColor(p: Float): Color = when {
    p >= 0.9f -> Danger
    p >= 0.75f -> Warn
    else -> Brand
}

@Composable
fun UsageRing(percent: Float, size: Dp, stroke: Dp = 12.dp, center: @Composable () -> Unit) {
    val track = MaterialTheme.colorScheme.surfaceContainerHighest
    val color = usageColor(percent)
    Box(Modifier.size(size), contentAlignment = Alignment.Center) {
        Canvas(Modifier.size(size)) {
            val st = Stroke(width = stroke.toPx(), cap = StrokeCap.Round)
            val inset = stroke.toPx() / 2
            val arcSize = androidx.compose.ui.geometry.Size(this.size.width - inset * 2, this.size.height - inset * 2)
            val topLeft = androidx.compose.ui.geometry.Offset(inset, inset)
            drawArc(track, 135f, 270f, false, topLeft, arcSize, style = st)
            drawArc(color, 135f, 270f * percent.coerceIn(0f, 1f), false, topLeft, arcSize, style = st)
        }
        center()
    }
}

@Composable
fun LabeledBar(label: String, value: String, percent: Float, color: Color = usageColor(percent)) {
    Column(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text(label, fontWeight = FontWeight.Medium)
            Text(value, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
        }
        Spacer(Modifier.height(6.dp))
        LinearProgressIndicator(
            progress = { percent.coerceIn(0f, 1f) },
            modifier = Modifier.fillMaxWidth().height(8.dp),
            color = color,
            trackColor = MaterialTheme.colorScheme.surfaceContainerHighest,
            strokeCap = StrokeCap.Round,
            gapSize = 0.dp,
            drawStopIndicator = {},
        )
    }
}

@Composable
fun PermissionCard(icon: ImageVector, title: String, text: String, button: String, onClick: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
    ) {
        Column(Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(icon, null, tint = Brand)
                Spacer(Modifier.size(10.dp))
                Text(title, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onPrimaryContainer)
            }
            Spacer(Modifier.height(6.dp))
            Text(text, fontSize = 14.sp, color = MaterialTheme.colorScheme.onPrimaryContainer)
            Spacer(Modifier.height(10.dp))
            Button(onClick = onClick) { Text(button) }
        }
    }
}

@Composable
fun InfoRow(label: String, value: String) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(0.45f))
        Text(value, modifier = Modifier.weight(0.55f), fontWeight = FontWeight.Medium)
    }
}
