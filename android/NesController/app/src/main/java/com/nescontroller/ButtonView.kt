package com.nescontroller

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View

/**
 * A single NES button. Supports multi-touch: tracks each pointer independently.
 * Draws as a circle (for square views) or rounded rectangle (for wide views).
 *
 * [bitValue] is the bit this button contributes to the controller bitmask (e.g. 0x10 for A).
 * [label] is the text drawn on the button (e.g. "A", "SELECT").
 * [onStateChanged] is called with the current bit contribution (0 or bitValue).
 */
class ButtonView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    var bitValue: Int = 0

    var label: String = ""
        set(value) {
            field = value
            invalidate()
        }

    var onStateChanged: ((Int) -> Unit)? = null

    // Track which pointers are currently down on this view
    private val activePointers = mutableSetOf<Int>()
    private var isPressed_ = false

    private val rectF = RectF()

    private val paintNormal = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#6B3A3A")
        style = Paint.Style.FILL
    }

    private val paintPressed = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#FF6B6B")
        style = Paint.Style.FILL
    }

    private val paintOutline = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.parseColor("#333333")
        style = Paint.Style.STROKE
        strokeWidth = 3f
    }

    private val paintText = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textAlign = Paint.Align.CENTER
        isFakeBoldText = true
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        rectF.set(4f, 4f, w - 4f, h - 4f)
        // Auto-size text to fit the smaller dimension
        paintText.textSize = minOf(w, h) * 0.30f
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        val fillPaint = if (isPressed_) paintPressed else paintNormal
        val cornerRadius = minOf(width, height) / 2f  // pill/circle shape

        canvas.drawRoundRect(rectF, cornerRadius, cornerRadius, fillPaint)
        canvas.drawRoundRect(rectF, cornerRadius, cornerRadius, paintOutline)

        if (label.isNotEmpty()) {
            val cx = width / 2f
            val cy = height / 2f
            val textOffset = (paintText.descent() + paintText.ascent()) / 2f
            canvas.drawText(label, cx, cy - textOffset, paintText)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val action = event.actionMasked
        val pointerIndex = event.actionIndex
        val pointerId = event.getPointerId(pointerIndex)

        when (action) {
            MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN -> {
                activePointers.add(pointerId)
                updateState()
            }

            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_POINTER_UP -> {
                activePointers.remove(pointerId)
                updateState()
            }

            MotionEvent.ACTION_CANCEL -> {
                activePointers.clear()
                updateState()
            }
        }

        return true
    }

    private fun updateState() {
        val nowPressed = activePointers.isNotEmpty()
        if (nowPressed != isPressed_) {
            isPressed_ = nowPressed
            invalidate()
            onStateChanged?.invoke(if (isPressed_) bitValue else 0)
        }
    }
}
