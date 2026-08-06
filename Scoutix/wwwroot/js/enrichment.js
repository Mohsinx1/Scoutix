// enrichment.js — handles single enrich, bulk enrich, and live status polling

const POLL_INTERVAL_MS = 5000;
let pollTimer = null;
let trackedJobIds = new Set(); // explicitly track every lead we enqueued this session

// ─── Single lead enrich ───────────────────────────────────────────────────────

function enrichLead(leadId, buttonEl) {
    if (buttonEl) {
        buttonEl.disabled = true;
        buttonEl.innerHTML = '<i class="bi bi-arrow-repeat enrich-spin me-1"></i>Queuing...';
    }

    fetch(`/Leads/EnrichLead?leadId=${leadId}`, { method: 'POST' })
        .then(r => r.json())
        .then(data => {
            if (data.queued) {
                setCellPending(leadId);
                trackedJobIds.add(leadId);
                startPollingIfNeeded();
            } else {
                // No website, already done, max attempts — show reason briefly
                const cell = document.getElementById(`enrich-cell-${leadId}`);
                if (cell) cell.innerHTML = `<span class="enrich-badge-none">${data.reason}</span>`;
            }
        })
        .catch(() => {
            if (buttonEl) {
                buttonEl.disabled = false;
                buttonEl.innerHTML = '<i class="bi bi-envelope me-1"></i>Enrich';
            }
        });
}

// ─── Bulk enrich ─────────────────────────────────────────────────────────────

function enrichSelected() {
    const checked = [...document.querySelectorAll('.lead-checkbox:checked')];
    const ids = checked.map(cb => parseInt(cb.value));
    if (ids.length === 0) return;

    const btn = document.getElementById('btnEnrichSelected');
    if (btn) btn.disabled = true;

    fetch('/Leads/EnrichLeads', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(ids)
    })
        .then(r => r.json())
        .then(data => {
            // Only mark leads the server actually enqueued — not skipped ones
            const enqueued = new Set(data.enqueuedIds || []);
            checked.forEach(cb => {
                const id = parseInt(cb.value);
                if (enqueued.has(id)) {
                    setCellPending(id);
                    trackedJobIds.add(id); // track so polling never gives up on them
                }
            });

            // Deselect all
            checked.forEach(cb => cb.checked = false);
            onCheckboxChange();

            startPollingIfNeeded();
        })
        .catch(() => {
            if (btn) btn.disabled = false;
        });
}

// ─── Checkbox state ───────────────────────────────────────────────────────────

function onCheckboxChange() {
    const checked = document.querySelectorAll('.lead-checkbox:checked').length;

    // Floating bar (fixed at bottom — always visible regardless of scroll position)
    const floatingBar  = document.getElementById('enrichFloatingBar');
    const floatingText = document.getElementById('floatingSelectedText');
    if (floatingBar) {
        floatingBar.style.display = checked > 0 ? 'flex' : 'none';
        if (floatingText) floatingText.textContent = `${checked} lead${checked === 1 ? '' : 's'} selected`;
    }
}

function copyEmail(email, btn) {
    navigator.clipboard.writeText(email).then(() => {
        const icon = btn.querySelector('i');
        icon.className = 'bi bi-check2';
        btn.title = 'Copied!';
        setTimeout(() => {
            icon.className = 'bi bi-copy';
            btn.title = 'Copy email';
        }, 2000);
    });
}

function copyPhone(phone, btn) {
    navigator.clipboard.writeText(phone).then(() => {
        const icon = btn.querySelector('i');
        icon.className = 'bi bi-check2';
        btn.title = 'Copied!';
        setTimeout(() => {
            icon.className = 'bi bi-copy';
            btn.title = 'Copy phone';
        }, 2000);
    });
}

function deselectAll() {
    document.querySelectorAll('.lead-checkbox:checked').forEach(cb => cb.checked = false);
    const selectAll = document.getElementById('selectAll');
    if (selectAll) selectAll.checked = false;
    onCheckboxChange();
}

document.addEventListener('DOMContentLoaded', () => {
    const selectAll = document.getElementById('selectAll');
    if (selectAll) {
        selectAll.addEventListener('change', () => {
            document.querySelectorAll('.lead-checkbox')
                .forEach(cb => cb.checked = selectAll.checked);
            onCheckboxChange();
        });
    }

    // Start polling immediately if any leads are already Pending
    startPollingIfNeeded();
});

// ─── Polling ──────────────────────────────────────────────────────────────────
// Uses recursive setTimeout (NOT setInterval) so each poll waits for the
// previous fetch to complete before scheduling the next one. setInterval
// fires blindly every N ms — if a fetch takes longer than the interval,
// two requests are in flight simultaneously, causing race conditions where
// one response clears the timer before the other has processed its results.

function startPollingIfNeeded() {
    if (getPendingLeadIds().length === 0) return;
    if (pollTimer) return; // already scheduled
    pollTimer = setTimeout(pollEnrichmentStatus, POLL_INTERVAL_MS);
}

function getPendingLeadIds() {
    // DOM-based: rows marked pending
    const domPending = [...document.querySelectorAll('tr.lead-row[data-enrichment-status="1"]')]
        .map(row => parseInt(row.dataset.leadId));

    // Union with explicitly tracked IDs — catches any the DOM missed
    const all = new Set(domPending);
    trackedJobIds.forEach(id => all.add(id));
    return [...all];
}

async function pollEnrichmentStatus() {
    pollTimer = null; // clear before doing any work

    const pending = getPendingLeadIds();
    if (pending.length === 0) return; // nothing left — stop

    try {
        const r = await fetch(`/Leads/EnrichmentStatus?ids=${pending.join(',')}`);
        const data = await r.json();

        Object.entries(data).forEach(([id, info]) => {
            const leadId = parseInt(id);
            // EnrichmentStatus enum: 0=None, 1=Pending, 2=Completed, 3=NoEmailFound, 4=Failed.
            // Only 2/3/4 are TERMINAL. A lead returns 0 (None) in the window between
            // bulk-enqueue and the Hangfire worker picking it up — the DB status isn't
            // flipped to Pending until the job runs. Treating "not 1" as resolved would
            // drop those still-queued leads from tracking, leaving them stuck on the
            // spinner forever (the job finishes later but nothing polls them again).
            const isTerminal = info.status === 2 || info.status === 3 || info.status === 4;
            if (isTerminal) {
                updateCell(leadId, info.status, info.email, info.emailStatus);
                updateOwnerCell(leadId, info.ownerName, info.ownerVerified);
                const row = document.querySelector(`tr.lead-row[data-lead-id="${leadId}"]`);
                if (row) row.dataset.enrichmentStatus = info.status;
                trackedJobIds.delete(leadId); // resolved — stop tracking
            }
        });
    } catch {
        // network blip — will retry on next schedule
    }

    // Schedule next poll ONLY after this one fully completed
    if (getPendingLeadIds().length > 0) {
        pollTimer = setTimeout(pollEnrichmentStatus, POLL_INTERVAL_MS);
    }
}

// ─── Cell helpers ─────────────────────────────────────────────────────────────

function setCellPending(leadId) {
    const cell = document.getElementById(`enrich-cell-${leadId}`);
    if (!cell) return;
    cell.innerHTML = '<span class="enrich-pending"><i class="bi bi-arrow-repeat enrich-spin me-1"></i>Finding...</span>';

    const row = document.querySelector(`tr.lead-row[data-lead-id="${leadId}"]`);
    if (row) row.dataset.enrichmentStatus = '1';
}

function updateCell(leadId, status, email, emailStatus) {
    const cell = document.getElementById(`enrich-cell-${leadId}`);
    if (!cell) return;

    // EnrichmentStatus enum: 2=Completed, 3=NoEmailFound, 4=Failed
    if (status === 2 && email) {
        cell.innerHTML = `
            <div class="enrich-done">
                <i class="bi bi-check-circle-fill enrich-icon-done me-1"></i>
                <a href="mailto:${email}" class="enrich-email-link">${email}</a>
                <button class="btn-copy-email" onclick="copyEmail('${email}', this)" title="Copy email">
                    <i class="bi bi-copy"></i>
                </button>
                ${emailTagHtml(emailStatus)}
            </div>`;
    } else if (status === 2 && !email) {
        // Job marked Completed but no email was saved — treat as no email found
        cell.innerHTML = '<span class="enrich-badge-none">No email found</span>';
    } else if (status === 3) {
        cell.innerHTML = '<span class="enrich-badge-none">No email found</span>';
    } else if (status === 4) {
        cell.innerHTML = `<button class="btn-enrich" onclick="enrichLead(${leadId}, this)">
            <i class="bi bi-arrow-clockwise me-1"></i>Retry
        </button>`;
    }
    // No else — leave the cell as-is for any unexpected status
    // (a spinner is an honest "still working" state; wrong button is confusing)
}

// EmailStatus enum (engine): 1=Verified, 2=CatchAll, else (0/Unknown etc.) = captured-from-website.
function emailTagHtml(emailStatus) {
    if (emailStatus === 1) return '<span class="email-tag email-verified">verified</span>';
    if (emailStatus === 2) return '<span class="email-tag email-catchall">catch-all</span>';
    return '<span class="email-tag email-web">from website</span>';
}

function updateOwnerCell(leadId, ownerName, ownerVerified) {
    const cell = document.getElementById(`owner-cell-${leadId}`);
    if (!cell) return;

    if (ownerName) {
        const badge = ownerVerified
            ? ' <span class="owner-verified" title="Confirmed by 2+ independent sources"><i class="bi bi-patch-check-fill"></i></span>'
            : '';
        cell.innerHTML = `<div class="owner-info"><span class="owner-name">${escapeHtml(ownerName)}</span>${badge}</div>`;
    } else {
        cell.innerHTML = '<span class="text-no-data">&mdash;</span>';
    }
}

function escapeHtml(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
}
