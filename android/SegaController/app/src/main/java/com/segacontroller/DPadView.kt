package com.segacontroller

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View

class DPadView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    companion object {
        private const val BIT_UP    = 0x01
        private const val BIT_DOWN  = 0x02
        private const val BIT_LEFT  = 0x04
        private const val BIT_RIGHT = 0x08
    }

    var onBitsChanged: ((Int) -> Unit)? = null

    private val pointerBits = mutableMapOf<Int, Int>()

    private val paintBase = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#444444")
        style = Paint.Style.FILL
    }

    private val paintHighlight = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#0066CC")
        style = Paint.Style.FILL
    }

    private val paintOutline = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#222222")
        style = Paint.Style.STROKE
        strokeWidth = 4f
    }

    private val paintCenterCap = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#2D2D2D")
        style = Paint.Style.FILL
    }

    private val paintMarker = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#E0E0E0")
        style = Paint.Style.FILL
    }

    private var cx = 0f
    private var cy = 0f
    private var rOuter = 0f
    private var rInner = 0f
    private var rMid = 0f
    private val sectorPaths = Array(8) { Path() }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        cx = w / 2f; cy = h / 2f
        rOuter = minOf(w, h) / 2f - 4f
        rInner = rOuter * 0.4f
        rMid = (rOuter + rInner) / 2f
        for (i in 0 until 8) sectorPaths[i] = getSectorPath(i, rOuter, rInner)
    }

    private fun getSectorPath(i: Int, rOut: Float, rIn: Float): Path {
        val path = Path()
        val centerAngle = i * 45f
        val sweepAngle = 41f
        val startAngle = centerAngle - sweepAngle / 2f
        path.arcTo(RectF(cx - rOut, cy - rOut, cx + rOut, cy + rOut), startAngle, sweepAngle)
        path.arcTo(RectF(cx - rIn, cy - rIn, cx + rIn, cy + rIn), startAngle + sweepAngle, -sweepAngle)
        path.close()
        return path
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawCircle(cx, cy, rInner, paintCenterCap)
        canvas.drawCircle(cx, cy, rInner, paintOutline)

        for (i in 0 until 8) {
            val sectorBits = sectorToBits(i)
            val isActive = pointerBits.values.any { it == sectorBits }
            val fillPaint = if (isActive) paintHighlight else paintBase
            canvas.drawPath(sectorPaths[i], fillPaint)
            canvas.drawPath(sectorPaths[i], paintOutline)

            val angleRad = Math.toRadians((i * 45).toDouble())
            val mx = cx + rMid * Math.cos(angleRad).toFloat()
            val my = cy + rMid * Math.sin(angleRad).toFloat()

            if (i % 2 == 1) {
                canvas.drawCircle(mx, my, rOuter * 0.06f, paintMarker)
            } else {
                canvas.save()
                canvas.translate(mx, my)
                canvas.rotate(i * 45f)
                val al = rOuter * 0.16f; val aw = rOuter * 0.16f
                val arrowPath = Path().apply {
                    moveTo(al / 2f, 0f)
                    lineTo(-al / 2f, -aw / 2f)
                    lineTo(-al / 2f, aw / 2f)
                    close()
                }
                canvas.drawPath(arrowPath, paintMarker)
                canvas.restore()
            }
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val action = event.actionMasked
        val pointerIndex = event.actionIndex
        val pointerId = event.getPointerId(pointerIndex)

        when (action) {
            MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN -> {
                pointerBits[pointerId] = hitTest(event.getX(pointerIndex), event.getY(pointerIndex))
                notifyChange()
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until event.pointerCount) {
                    val pid = event.getPointerId(i)
                    val newBits = hitTest(event.getX(i), event.getY(i))
                    if (pointerBits[pid] != newBits) pointerBits[pid] = newBits
                }
                notifyChange()
            }
            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_POINTER_UP -> { pointerBits.remove(pointerId); notifyChange() }
            MotionEvent.ACTION_CANCEL -> { pointerBits.clear(); notifyChange() }
        }
        return true
    }

    private fun hitTest(x: Float, y: Float): Int {
        val dx = x - cx; val dy = y - cy
        val dist = Math.sqrt((dx * dx + dy * dy).toDouble()).toFloat()
        if (dist < rInner || dist > rOuter * 1.25f) return 0
        var angleDeg = Math.toDegrees(Math.atan2(dy.toDouble(), dx.toDouble())).toFloat()
        if (angleDeg < 0) angleDeg += 360f
        val sector = (((angleDeg + 22.5f) % 360f) / 45f).toInt()
        return sectorToBits(sector)
    }

    private fun sectorToBits(sector: Int): Int = when (sector) {
        0 -> BIT_RIGHT
        1 -> BIT_DOWN or BIT_RIGHT
        2 -> BIT_DOWN
        3 -> BIT_DOWN or BIT_LEFT
        4 -> BIT_LEFT
        5 -> BIT_UP or BIT_LEFT
        6 -> BIT_UP
        7 -> BIT_UP or BIT_RIGHT
        else -> 0
    }

    private fun notifyChange() {
        var union = 0
        for (bits in pointerBits.values) union = union or bits
        invalidate()
        onBitsChanged?.invoke(union)
    }
}
