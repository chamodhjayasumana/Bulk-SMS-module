package com.bulksms.gateway.ui

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.ArrayAdapter
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.bulksms.gateway.R
import com.bulksms.gateway.SmsGatewayApp
import com.bulksms.gateway.ai.LocalCampaignAssistant
import com.bulksms.gateway.databinding.ActivityAiAssistantBinding
import com.google.android.material.button.MaterialButton

/**
 * Phone screen for campaign wording. Suggestions are written on the phone and are not sent automatically.
 */
class AiAssistantActivity : AppCompatActivity() {

    private lateinit var binding: ActivityAiAssistantBinding
    private val app get() = application as SmsGatewayApp
    private val assistant = LocalCampaignAssistant()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityAiAssistantBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.spinnerLanguage.adapter = spinnerAdapter(listOf("English", "Sinhala", "Tamil"))
        binding.spinnerTone.adapter = spinnerAdapter(listOf("Professional", "Friendly", "Urgent", "Promotional"))
        binding.spinnerSegments.adapter = spinnerAdapter(listOf("1", "2", "3"))
        binding.spinnerSegments.setSelection(1)
        binding.btnGenerate.setOnClickListener { generate() }
    }

    private fun generate() {
        val description = binding.inputDescription.text?.toString()?.trim().orEmpty()
        val language = when (binding.spinnerLanguage.selectedItemPosition) {
            1 -> "si"
            2 -> "ta"
            else -> "en"
        }
        val tone = when (binding.spinnerTone.selectedItemPosition) {
            1 -> "friendly"
            2 -> "urgent"
            3 -> "promotional"
            else -> "professional"
        }
        val segments = binding.spinnerSegments.selectedItemPosition + 1
        val fields = ArrayList<String>()
        if (binding.checkName.isChecked) fields.add("name")
        if (binding.checkOrder.isChecked) fields.add("orderNumber")
        if (binding.checkAmount.isChecked) fields.add("amount")
        val sender = binding.inputSender.text?.toString()?.trim().orEmpty()

        val draft = assistant.draft(
            description,
            language,
            sender,
            segments,
            tone,
            binding.checkCallToAction.isChecked,
            fields
        )
        binding.suggestionList.removeAllViews()
        if (draft.suggestions.isEmpty()) {
            showStatus(draft.safetyWarnings.firstOrNull() ?: "Could not create suggestions.")
            return
        }
        showStatus("Review each suggestion before sending. The computer is not needed.")
        render(draft)
    }

    private fun render(draft: LocalCampaignAssistant.Draft) {
        val list = binding.suggestionList
        list.removeAllViews()
        draft.safetyWarnings.forEach { warning ->
            list.addView(note(warning, getColor(R.color.fail)))
        }
        draft.suggestions.filter { it.message.isNotBlank() }.forEach { suggestion ->
            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(24, 24, 24, 24)
                setBackgroundResource(R.drawable.card_bg)
                val params = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                )
                params.topMargin = 16
                layoutParams = params
            }
            card.addView(note(suggestion.message, getColor(R.color.teal_900)))
            card.addView(note("Characters: ${suggestion.characterCount} · SMS segments: ${suggestion.segments}", getColor(R.color.muted)))
            suggestion.warnings.forEach { card.addView(note(it, getColor(R.color.fail))) }
            val use = MaterialButton(this).apply {
                text = "Use this message"
                setOnClickListener {
                    app.bulkSession.message = suggestion.message
                    toast("Copied into CSV send. Review it before sending.")
                    startActivity(Intent(this@AiAssistantActivity, BulkSendActivity::class.java))
                }
            }
            card.addView(use)
            list.addView(card)
        }
    }

    private fun note(text: String, color: Int): TextView {
        return TextView(this).apply {
            this.text = text
            setTextColor(color)
            textSize = 15f
            setPadding(0, 8, 0, 8)
        }
    }

    private fun showStatus(text: String) {
        binding.txtStatus.text = text
        binding.txtStatus.visibility = View.VISIBLE
    }

    private fun spinnerAdapter(items: List<String>): ArrayAdapter<String> {
        return ArrayAdapter(this, android.R.layout.simple_spinner_item, items).also {
            it.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        }
    }

    private fun toast(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
    }
}
