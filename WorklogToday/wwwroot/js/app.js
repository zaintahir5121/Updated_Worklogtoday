(() => {
    "use strict";
    const $ = (s, r = document) => r.querySelector(s);
    const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
    const tokenEl = $('input[name="__RequestVerificationToken"]');
    const TOKEN = tokenEl ? tokenEl.value : '';
    const body = $('.app-body');
    const RANGE = { from: body.dataset.from, to: body.dataset.to, today: body.dataset.today };

    const CAT = ["Development","Meeting","Support","Review","Planning","Research","Documentation","Other"];
    const STAT = ["Planned","InProgress","Done","Blocked"];
    const catCls = c => "p-" + CAT[c].toLowerCase();
    const statCls = s => "s-" + STAT[s].toLowerCase();
    const esc = s => (s ?? '').replace(/[&<>"]/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[m]));

    async function api(method, url, data) {
        const opt = { method, headers: { 'RequestVerificationToken': TOKEN } };
        if (data !== undefined) { opt.headers['Content-Type'] = 'application/json'; opt.body = JSON.stringify(data); }
        const res = await fetch(url, opt);
        if (!res.ok) { let e = {}; try { e = await res.json(); } catch {} throw new Error(e.error || ('Request failed: ' + res.status)); }
        return res.status === 204 ? null : res.json();
    }

    let toastT;
    function toast(msg, dur = 2200) {
        const t = $('#toast'); t.textContent = msg; t.classList.add('show');
        clearTimeout(toastT); toastT = setTimeout(() => t.classList.remove('show'), dur);
    }

    // ---------- Tabs with slide animation ----------
    const TAB_ORDER = ['notes', 'tasks', 'timesheet', 'reports'];
    let currentTab = 'notes';

    function activateTab(name, direction) {
        const isMobile = window.innerWidth <= 640;
        const prev = currentTab;
        currentTab = name;

        $$('.app-tab').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
        $$('.bottom-nav-item').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
        localStorage.setItem('wt_tab', name);
        if (name === 'reports') loadReports();

        const fab = $('#fabGroup') || $('#mobileFab');
        if (fab) fab.style.display = name === 'notes' ? '' : 'none';

        // Update swipe indicator dots
        $$('.tab-dot').forEach(d => d.classList.toggle('active', d.dataset.tab === name));

        if (isMobile && prev !== name) {
            // determine slide direction
            const fromIdx = TAB_ORDER.indexOf(prev);
            const toIdx = TAB_ORDER.indexOf(name);
            const goingRight = (direction === 'right') || (direction === undefined && toIdx > fromIdx);

            $$('.tab-pane').forEach(p => {
                const pName = p.id.replace('pane-', '');
                p.classList.remove('active', 'slide-left', 'slide-right');
                if (pName === name) {
                    // new pane: start offscreen in direction of travel
                    p.style.transition = 'none';
                    p.classList.add(goingRight ? 'slide-right' : 'slide-left');
                    // force reflow then animate in
                    p.offsetHeight;
                    p.style.transition = '';
                    p.classList.remove('slide-left', 'slide-right');
                    p.classList.add('active');
                } else if (pName === prev) {
                    // old pane: slide out opposite direction
                    p.classList.add(goingRight ? 'slide-left' : 'slide-right');
                }
            });
        } else {
            $$('.tab-pane').forEach(p => {
                p.classList.remove('slide-left', 'slide-right');
                p.classList.toggle('active', p.id === 'pane-' + name);
            });
        }
    }

    $$('.app-tab').forEach(b => b.addEventListener('click', () => activateTab(b.dataset.tab)));
    $$('.bottom-nav-item').forEach(b => b.addEventListener('click', () => activateTab(b.dataset.tab)));
    const urlParams = new URLSearchParams(location.search);
    const urlTab = urlParams.get('tab');
    const savedTab = urlTab || localStorage.getItem('wt_tab');
    currentTab = (savedTab && TAB_ORDER.includes(savedTab)) ? savedTab : 'notes';
    activateTab(currentTab);

    // Show confirmation when redirected back from PWA share target
    if (urlParams.get('shared') === '1') {
        const sharedTab = urlParams.get('tab');
        if (sharedTab && TAB_ORDER.includes(sharedTab)) activateTab(sharedTab);
        const label = sharedTab === 'tasks' ? 'Task logged from share ✓' : 'Saved to Notes from share ✓';
        setTimeout(() => toast(label, 3000), 600);
        history.replaceState({}, '', '/app');
    }

    // ---------- Swipe between tabs (mobile) ----------
    (function () {
        let startX = 0, startY = 0, startTime = 0;
        const appBody = $('.app-body');
        if (!appBody) return;

        appBody.addEventListener('touchstart', e => {
            startX = e.touches[0].clientX;
            startY = e.touches[0].clientY;
            startTime = Date.now();
        }, { passive: true });

        appBody.addEventListener('touchend', e => {
            if (window.innerWidth > 640) return;
            const dx = e.changedTouches[0].clientX - startX;
            const dy = e.changedTouches[0].clientY - startY;
            const dt = Date.now() - startTime;

            // Ignore slow swipes, vertical scrolls, tiny swipes
            if (dt > 400 || Math.abs(dx) < 50 || Math.abs(dy) > Math.abs(dx) * 0.9) return;

            const idx = TAB_ORDER.indexOf(currentTab);
            if (dx < 0 && idx < TAB_ORDER.length - 1) {
                // swipe left → next tab
                activateTab(TAB_ORDER[idx + 1], 'right');
            } else if (dx > 0 && idx > 0) {
                // swipe right → prev tab
                activateTab(TAB_ORDER[idx - 1], 'left');
            }
        }, { passive: true });
    })();

    // ---------- Pull-to-refresh (mobile) ----------
    (function () {
        let startY = 0, pulling = false;
        const ptr = document.createElement('div');
        ptr.id = 'ptr';
        ptr.innerHTML = '<i class="bi bi-arrow-clockwise"></i>';
        document.body.prepend(ptr);

        document.addEventListener('touchstart', e => {
            if (window.innerWidth > 640) return;
            startY = e.touches[0].clientY;
            pulling = window.scrollY === 0;
        }, { passive: true });

        document.addEventListener('touchmove', e => {
            if (!pulling || window.innerWidth > 640) return;
            const dy = e.touches[0].clientY - startY;
            if (dy > 0) ptr.style.transform = `translateY(${Math.min(dy * 0.4, 56)}px)`;
        }, { passive: true });

        document.addEventListener('touchend', e => {
            if (!pulling || window.innerWidth > 640) return;
            const dy = e.changedTouches[0].clientY - startY;
            ptr.style.transform = '';
            pulling = false;
            if (dy > 80) {
                ptr.classList.add('spinning');
                setTimeout(() => { ptr.classList.remove('spinning'); location.reload(); }, 400);
            }
        }, { passive: true });
    })();

    // ---------- Mobile FAB ----------
    const mobileFab = $('#mobileFab');
    const fabGroup = $('#fabGroup');
    let fabOpen = false;

    function toggleFab(open) {
        fabOpen = open;
        const fabVoice = $('#fabVoice'), fabText = $('#fabText'), fabSmart2 = $('#fabSmart');
        if (fabVoice) fabVoice.style.display = open ? '' : 'none';
        if (fabText) fabText.style.display = open ? '' : 'none';
        if (fabSmart2) fabSmart2.style.display = open ? '' : 'none';
        if (mobileFab) {
            mobileFab.querySelector('i').className = open ? 'bi bi-x-lg' : 'bi bi-plus';
        }
    }

    if (mobileFab) {
        mobileFab.addEventListener('click', e => {
            e.stopPropagation();
            if (fabOpen) {
                toggleFab(false);
            } else {
                toggleFab(true);
            }
        });
    }

    const fabText = $('#fabText');
    if (fabText) {
        fabText.addEventListener('click', e => {
            e.stopPropagation();
            toggleFab(false);
            const composer = $('#composer');
            const body = $('#cBody');
            if (composer && body) {
                composer.classList.add('expanded');
                body.focus();
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
        });
    }

    const fabVoice = $('#fabVoice');
    if (fabVoice) {
        fabVoice.addEventListener('click', e => {
            e.stopPropagation();
            toggleFab(false);
            openVoiceModal();
        });
    }

    document.addEventListener('click', e => {
        if (fabOpen && fabGroup && !fabGroup.contains(e.target)) toggleFab(false);
    });

    // ---------- PWA install ----------
    let deferredPrompt = null;
    const installBtn = document.getElementById('installBtn');
    window.addEventListener('beforeinstallprompt', e => {
        e.preventDefault(); deferredPrompt = e;
        if (installBtn) installBtn.style.display = '';
    });
    if (installBtn) installBtn.addEventListener('click', async () => {
        if (!deferredPrompt) { toast('Use your browser menu → Install app'); return; }
        deferredPrompt.prompt();
        await deferredPrompt.userChoice;
        deferredPrompt = null; installBtn.style.display = 'none';
    });
    window.addEventListener('appinstalled', () => { if (installBtn) installBtn.style.display = 'none'; toast('Installed! Find worklog on your home screen.'); });

    // ---------- Composer ----------
    const composer = $('#composer'), cTitle = $('#cTitle'), cBody = $('#cBody'), cLabels = $('#cLabels');
    let curColor = '#ffffff';
    let composerSaving = false;

    function expand() { composer.classList.add('expanded'); }

    async function collapseAndSave() {
        if (composerSaving) return;
        if (!cTitle.value && !cBody.value) {
            // Nothing typed — just collapse
            composer.classList.remove('expanded');
            return;
        }
        composerSaving = true;
        try {
            const note = await api('POST', '/api/notes', { title: cTitle.value, content: cBody.value, colorHex: curColor, labels: cLabels.value });
            addNoteToDom(note, true);
            cTitle.value = cBody.value = cLabels.value = '';
            cBody.style.height = 'auto'; curColor = '#ffffff'; composer.style.background = '';
            $$('#swatches .sw').forEach(s => s.classList.toggle('active', s.dataset.color === '#ffffff'));
            composer.classList.remove('expanded');
            $('#notesEmpty').style.display = 'none';
        } catch (e) { toast(e.message); }
        finally { composerSaving = false; }
    }

    cBody.addEventListener('focus', expand);
    cTitle.addEventListener('focus', expand);
    cBody.addEventListener('input', () => { cBody.style.height = 'auto'; cBody.style.height = cBody.scrollHeight + 'px'; });
    document.addEventListener('click', e => { if (!composer.contains(e.target)) collapseAndSave(); });
    // Escape key closes composer and saves
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && composer.classList.contains('expanded') && !activeCard) collapseAndSave();
    });

    $('#swatches').addEventListener('click', e => {
        const sw = e.target.closest('.sw'); if (!sw) return;
        curColor = sw.dataset.color;
        $$('#swatches .sw').forEach(s => s.classList.toggle('active', s === sw));
        composer.style.background = curColor === '#ffffff' ? '' : curColor;
    });

    $('#aiLabelBtn').addEventListener('click', async () => {
        if (!cBody.value && !cTitle.value) return toast('Write something first');
        const btn = $('#aiLabelBtn'); btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> …';
        try {
            const r = await api('POST', '/api/notes/suggest-labels', { title: cTitle.value, content: cBody.value });
            cLabels.value = r.labels; toast('Smart AI tagged your note');
        } catch (e) { toast(e.message); }
        finally { btn.disabled = false; btn.innerHTML = '<i class="bi bi-magic"></i> AI tags'; }
    });

    const COLORS = ["#ffffff","#fff8c5","#d3f9d8","#dbeafe","#fbe4ff","#ffe8cc","#ffd6d6"];

    function noteCardHtml(n) {
        const labels = (n.labels || '').split(',').map(s => s.trim()).filter(Boolean);
        const swatches = COLORS.map(c =>
            `<span class="nsw${c === n.colorHex ? ' active' : ''}" style="background:${c}" data-color="${c}"></span>`
        ).join('');
        const audioBlock = n.audioUrl ? `
          <div class="note-audio-block">
            <span class="note-audio-chip"><i class="bi bi-mic-fill"></i> Voice</span>
            <audio controls preload="metadata" src="${esc(n.audioUrl)}"></audio>
            ${n.transcript ? `<p class="note-audio-transcript">${esc(n.transcript)}</p>` : ''}
          </div>` : '';
        const bodyText = n.audioUrl && (n.content === '[Voice note]' || n.content === '[Shared voice note]') ? '' : n.content || '';
        return `
          <button class="mini-btn pin ${n.isPinned ? 'on' : ''}" type="button" data-act="pin" title="Pin note"><i class="bi ${n.isPinned ? 'bi-pin-angle-fill' : 'bi-pin-angle'}"></i></button>
          <div class="ntitle-edit" contenteditable="true" data-placeholder="Title" spellcheck="true">${esc(n.title || '')}</div>
          ${audioBlock}
          <div class="ntext-edit" contenteditable="true" data-placeholder="Add a note…" spellcheck="true">${esc(bodyText)}</div>
          <div class="nlabels">${labels.map(l => `<span class="nlabel">${esc(l)}</span>`).join('')}</div>
          <div class="note-expanded-tools">
            <div class="note-swatches">${swatches}</div>
            <input class="nlabels-input" value="${esc(n.labels || '')}" placeholder="labels…" title="Comma-separated labels" />
          </div>
          <div class="nactions">
            <button class="mini-btn" type="button" data-act="popout" title="Pop out sticky"><i class="bi bi-window-stack"></i></button>
            <button class="mini-btn" type="button" data-act="extract" title="Extract tasks with Smart AI"><i class="bi bi-list-task"></i></button>
            <button class="mini-btn" type="button" data-act="archive" title="Archive"><i class="bi bi-archive"></i></button>
            <button class="mini-btn" type="button" data-act="delete" title="Delete"><i class="bi bi-trash"></i></button>
            <button class="mini-btn note-close-btn" type="button" data-act="close" title="Close"><i class="bi bi-x-lg"></i></button>
          </div>`;
    }
    function makeNoteEl(n) {
        const el = document.createElement('div');
        el.className = 'note' + (n.isPinned ? ' pinned' : '');
        el.dataset.id = n.id; el.dataset.color = n.colorHex; el.dataset.labels = n.labels || '';
        el.style.background = n.colorHex;
        el.innerHTML = noteCardHtml(n);
        return el;
    }
    function addNoteToDom(n, prepend) {
        const target = n.isPinned ? $('#pinnedNotes') : $('#otherNotes');
        const el = makeNoteEl(n);
        prepend ? target.prepend(el) : target.append(el);
        refreshSections();
    }
    function refreshSections() {
        const hasPinned = $('#pinnedNotes').children.length > 0;
        const hasOther = $('#otherNotes').children.length > 0;
        $('#pinnedWrap').style.display = hasPinned ? '' : 'none';
        $('#othersLabel').style.display = (hasPinned && hasOther) ? '' : 'none';
        $('#notesEmpty').style.display = (hasPinned || hasOther) ? 'none' : '';
    }

    // ── Note backdrop (Google Keep style) ──────────────────────────────────
    const noteBackdrop = document.createElement('div');
    noteBackdrop.className = 'note-backdrop';
    document.body.appendChild(noteBackdrop);
    noteBackdrop.addEventListener('click', () => { if (activeCard) closeNoteInline(activeCard); });

    // ── Inline note editing (Google Keep-style) ──────────────────────────────
    let activeCard = null;
    let autoSaveTimer = null;

    function openNoteInline(card) {
        if (activeCard === card) return;
        if (activeCard) closeNoteInline(activeCard);
        activeCard = card;
        card.classList.add('note-open');
        noteBackdrop.classList.add('on');
        card.querySelector('.ntext-edit').focus();
    }

    async function closeNoteInline(card) {
        if (!card || !card.classList.contains('note-open')) return;
        card.classList.remove('note-open');
        noteBackdrop.classList.remove('on');
        if (activeCard === card) activeCard = null;
        clearTimeout(autoSaveTimer);
        await saveNoteInline(card);
    }

    async function saveNoteInline(card, silent = false) {
        const id = card.dataset.id;
        const title = card.querySelector('.ntitle-edit').textContent.trim();
        const content = card.querySelector('.ntext-edit').textContent.trim();
        const colorHex = card.dataset.color;
        const labels = card.querySelector('.nlabels-input').value.trim();

        // Update label chips inline
        const chipsEl = card.querySelector('.nlabels');
        if (chipsEl) {
            const chips = labels.split(',').map(s => s.trim()).filter(Boolean);
            chipsEl.innerHTML = chips.map(l => `<span class="nlabel">${esc(l)}</span>`).join('');
        }
        card.dataset.labels = labels;

        try {
            await api('PUT', `/api/notes/${id}`, { title, content, colorHex, labels });
            if (!silent) toast('Saved ✓', 1200);
        } catch (e) { toast('⚠ ' + e.message); }
    }

    function scheduleAutoSave(card) {
        clearTimeout(autoSaveTimer);
        autoSaveTimer = setTimeout(() => saveNoteInline(card, true), 1500);
    }

    // Auto-save on typing inside an open card
    document.addEventListener('input', e => {
        const card = e.target.closest('.note.note-open');
        if (card) scheduleAutoSave(card);
    });

    // Single click handler for all note interactions
    document.addEventListener('click', async e => {
        const card = e.target.closest('.note');

        // Click outside any card — close active
        if (!card) {
            if (activeCard) closeNoteInline(activeCard);
            return;
        }

        // Color swatch
        const swatch = e.target.closest('.nsw');
        if (swatch) {
            const color = swatch.dataset.color;
            card.style.background = color;
            card.dataset.color = color;
            card.querySelectorAll('.nsw').forEach(s => s.classList.toggle('active', s.dataset.color === color));
            scheduleAutoSave(card);
            return;
        }

        // Action buttons
        const btn = e.target.closest('[data-act]');
        if (btn) {
            const id = card.dataset.id;
            const act = btn.dataset.act;
            try {
                if (act === 'close') {
                    await closeNoteInline(card);
                } else if (act === 'pin') {
                    await closeNoteInline(card);
                    const n = await api('POST', `/api/notes/${id}/pin`);
                    card.remove(); addNoteToDom(n, true);
                    toast(n.isPinned ? 'Pinned' : 'Unpinned');
                } else if (act === 'archive') {
                    await api('POST', `/api/notes/${id}/archive`);
                    card.remove(); refreshSections(); toast('Archived');
                } else if (act === 'delete') {
                    if (!confirm('Delete this note?')) return;
                    await api('DELETE', `/api/notes/${id}`);
                    card.remove(); refreshSections(); toast('Deleted');
                } else if (act === 'popout') {
                    openSticky(id);
                } else if (act === 'extract') {
                    openExtractModal(id, btn);
                }
            } catch (err) { toast(err.message); }
            return;
        }

        // Click on card body → open inline edit
        openNoteInline(card);
    });

    // Escape key closes active card
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && activeCard) closeNoteInline(activeCard);
    });

    // ---------- Smart AI: Extract tasks from note ----------
    const extractModal = $('#extractModal');
    let extractedTasks = [];

    async function openExtractModal(noteId, triggerBtn) {
        const origHtml = triggerBtn.innerHTML;
        triggerBtn.disabled = true; triggerBtn.innerHTML = '<i class="bi bi-hourglass-split"></i>';
        $('#extractList').innerHTML = '<div style="text-align:center;padding:24px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:24px;display:block;margin-bottom:8px"></i>Smart AI is reading your note…</div>';
        extractModal.classList.add('open');
        try {
            const r = await api('POST', `/api/notes/${noteId}/extract-tasks`);
            extractedTasks = r.tasks || [];
            if (!extractedTasks.length) {
                $('#extractList').innerHTML = '<div class="empty"><p>No clear action items found. Try adding more specific tasks to your note.</p></div>';
                return;
            }
            $('#extractList').innerHTML = extractedTasks.map((t, i) => `
              <label class="extract-item" style="display:flex;align-items:flex-start;gap:10px;padding:10px 0;border-bottom:1px solid var(--border);cursor:pointer">
                <input type="checkbox" checked data-idx="${i}" style="margin-top:3px;width:15px;height:15px;flex-shrink:0" />
                <div style="flex:1">
                  <div style="font-weight:600;font-size:14px">${esc(t.task)}</div>
                  <div style="font-size:12px;color:var(--muted);margin-top:2px">${CAT[t.category] || 'Development'} · ${t.hours}h</div>
                </div>
              </label>`).join('');
        } catch (err) {
            $('#extractList').innerHTML = `<div class="empty"><p>⚠ ${esc(err.message)}</p></div>`;
        } finally {
            triggerBtn.disabled = false; triggerBtn.innerHTML = origHtml;
        }
    }

    $('#addExtractedBtn').addEventListener('click', async () => {
        const checked = $$('#extractList input[type=checkbox]:checked');
        if (!checked.length) return toast('Select at least one task');
        const btn = $('#addExtractedBtn'); btn.disabled = true;
        let added = 0;
        for (const cb of checked) {
            const t = extractedTasks[+cb.dataset.idx];
            if (!t) continue;
            try {
                const w = await api('POST', '/api/work', {
                    task: t.task, project: '', category: t.category, status: 2,
                    hours: t.hours, date: RANGE.today, billable: true, notes: null
                });
                if (w.date >= RANGE.from && w.date <= RANGE.to) {
                    const tr = document.createElement('tr');
                    setRowData(tr, w);
                    $('#taskBody').append(tr);
                }
                added++;
            } catch { /* skip failed items */ }
        }
        taskEmptyCheck();
        extractModal.classList.remove('open');
        btn.disabled = false;
        toast(`${added} task${added !== 1 ? 's' : ''} added to your log`);
        activateTab('tasks');
    });

    // ---------- Sticky pop-out windows ----------
    let stickyX = 40, stickyY = 40;
    function openSticky(id) {
        const w = 300, h = 330;
        const feat = `popup=yes,width=${w},height=${h},left=${window.screenX + stickyX},top=${window.screenY + stickyY},menubar=no,toolbar=no,location=no,status=no`;
        window.open('/sticky/' + id, 'sticky-' + id, feat);
        stickyX = (stickyX + 36) % 320; stickyY = (stickyY + 30) % 260;
    }
    const newStickyBtn = document.getElementById('newStickyBtn');
    if (newStickyBtn) newStickyBtn.addEventListener('click', () =>
        window.open('/sticky/new', 'sticky-new-' + Date.now(), 'popup=yes,width=300,height=330'));

    // ---------- Voice Notes ----------
    let mediaRecorder = null, audioChunks = [], voiceBlob = null, voiceTranscript = '';
    let voiceTimerInterval = null, voiceSeconds = 0;
    let recognition = null;

    function openVoiceModal() {
        voiceBlob = null; voiceTranscript = ''; audioChunks = [];
        $('#voiceStatus').textContent = 'Press the button and start speaking.';
        $('#voiceTimer').style.display = 'none';
        $('#voiceWaveWrap').style.display = 'none';
        $('#voiceTranscriptPreview').style.display = 'none';
        $('#voiceTranscriptPreview').textContent = '';
        $('#voiceSaveBtn').disabled = true;
        const btn = $('#voiceRecordBtn');
        btn.classList.remove('recording');
        btn.innerHTML = '<i class="bi bi-mic-fill"></i>';
        $('#voiceModal').classList.add('open');
    }

    function formatVoiceTime(s) {
        return `${Math.floor(s/60)}:${String(s%60).padStart(2,'0')}`;
    }

    $('#voiceNoteBtn').addEventListener('click', e => { e.stopPropagation(); openVoiceModal(); });

    function stopRecordingIfActive() {
        if (mediaRecorder && mediaRecorder.state === 'recording') {
            mediaRecorder.stop();
            if (recognition) { try { recognition.stop(); } catch {} }
            clearInterval(voiceTimerInterval);
            $('#voiceRecordBtn').classList.remove('recording');
            $('#voiceRecordBtn').innerHTML = '<i class="bi bi-mic-fill"></i>';
        }
    }

    $('#voiceModal').addEventListener('click', e => {
        if (e.target === $('#voiceModal')) stopRecordingIfActive();
    });
    $$('[data-close]', $('#voiceModal')).forEach(b => b.addEventListener('click', stopRecordingIfActive));

    $('#voiceRecordBtn').addEventListener('click', async () => {
        const btn = $('#voiceRecordBtn');
        if (mediaRecorder && mediaRecorder.state === 'recording') {
            // Stop recording
            mediaRecorder.stop();
            if (recognition) { try { recognition.stop(); } catch {} }
            clearInterval(voiceTimerInterval);
            btn.classList.remove('recording');
            btn.innerHTML = '<i class="bi bi-mic-fill"></i>';
            $('#voiceStatus').textContent = 'Recording saved. Press Save to add as a note.';
            $('#voiceWaveWrap').style.display = 'none';
            return;
        }

        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            audioChunks = []; voiceTranscript = ''; voiceSeconds = 0;

            const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus'
                : MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm'
                : MediaRecorder.isTypeSupported('audio/ogg') ? 'audio/ogg'
                : '';
            mediaRecorder = new MediaRecorder(stream, mimeType ? { mimeType } : {});
            mediaRecorder.ondataavailable = e => { if (e.data.size > 0) audioChunks.push(e.data); };
            mediaRecorder.onstop = () => {
                stream.getTracks().forEach(t => t.stop());
                voiceBlob = new Blob(audioChunks, { type: mediaRecorder.mimeType || 'audio/webm' });
                $('#voiceSaveBtn').disabled = false;
                const tp = $('#voiceTranscriptPreview');
                if (voiceTranscript) { tp.textContent = voiceTranscript; tp.style.display = ''; }
            };
            mediaRecorder.start(250);

            btn.classList.add('recording');
            btn.innerHTML = '<i class="bi bi-stop-fill"></i>';
            $('#voiceStatus').textContent = 'Recording… tap the button to stop.';
            $('#voiceTimer').style.display = '';
            $('#voiceTimer').textContent = '0:00';
            $('#voiceWaveWrap').style.display = '';
            voiceTimerInterval = setInterval(() => {
                voiceSeconds++;
                $('#voiceTimer').textContent = formatVoiceTime(voiceSeconds);
                if (voiceSeconds >= 300) { // 5 min max
                    $('#voiceRecordBtn').click();
                }
            }, 1000);

            // Web Speech API for live transcription
            if ('webkitSpeechRecognition' in window || 'SpeechRecognition' in window) {
                const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
                recognition = new SR();
                recognition.continuous = true;
                recognition.interimResults = true;
                recognition.lang = navigator.language || 'en-US';
                let finalTranscript = '';
                recognition.onresult = e => {
                    let interim = '';
                    for (let i = e.resultIndex; i < e.results.length; i++) {
                        if (e.results[i].isFinal) finalTranscript += e.results[i][0].transcript + ' ';
                        else interim += e.results[i][0].transcript;
                    }
                    voiceTranscript = (finalTranscript + interim).trim();
                    const tp = $('#voiceTranscriptPreview');
                    tp.textContent = voiceTranscript || '';
                    if (voiceTranscript) tp.style.display = '';
                };
                recognition.onerror = () => {};
                try { recognition.start(); } catch {}
            }
        } catch (err) {
            toast('Microphone access denied. Please allow microphone in browser settings.');
        }
    });

    $('#voiceSaveBtn').addEventListener('click', async () => {
        if (!voiceBlob) return;
        const btn = $('#voiceSaveBtn');
        btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Saving…';
        try {
            const ext = voiceBlob.type.includes('ogg') ? '.ogg' : voiceBlob.type.includes('mp4') ? '.m4a' : '.webm';
            const fd = new FormData();
            fd.append('audio', voiceBlob, 'voice' + ext);
            if (voiceTranscript) fd.append('transcript', voiceTranscript);
            const res = await fetch('/api/notes/voice', {
                method: 'POST',
                headers: { 'RequestVerificationToken': TOKEN },
                body: fd
            });
            if (!res.ok) { const e = await res.json().catch(() => ({})); throw new Error(e.error || 'Upload failed'); }
            const note = await res.json();
            addNoteToDom(note, true);
            $('#notesEmpty').style.display = 'none';
            $('#voiceModal').classList.remove('open');
            toast('Voice note saved');
            if (currentTab !== 'notes') activateTab('notes');
        } catch (e) {
            toast(e.message);
            btn.disabled = false; btn.innerHTML = '<i class="bi bi-check-lg"></i> Save note';
        }
    });

    // ---------- Smart Meeting Note ----------
    let smRecorder = null, smAudioChunks = [], smRecognition = null;
    let smTimerInterval = null, smSeconds = 0;
    let smTranscript = '', smFinalTranscript = '';
    let smRecording = false;

    function openSmartMeeting() {
        smTranscript = ''; smFinalTranscript = ''; smAudioChunks = []; smSeconds = 0;
        $('#smRecordPhase').style.display = '';
        $('#smGeneratingPhase').style.display = 'none';
        $('#smPreviewPhase').style.display = 'none';
        $('#smStopBtn').style.display = 'none';
        $('#smSaveBtn').style.display = 'none';
        $('#smCancelBtn').style.display = '';
        $('#smStatus').innerHTML = 'Press <strong>Start</strong> to record your meeting. Say <strong>"end meeting"</strong> to stop automatically.';
        $('#smTimer').style.display = 'none';
        $('#smWaveWrap').style.display = 'none';
        $('#smTranscriptBox').style.display = 'none';
        $('#smTranscriptText').textContent = '';
        $('#smHint').style.display = '';
        $('#smRecordBtn').classList.remove('recording');
        $('#smRecordBtn').innerHTML = '<i class="bi bi-play-fill"></i>';
        $('#smartMeetingModal').classList.add('open');
        smRecording = false;
    }

    function closeSmartMeeting() {
        stopSmartRecording();
        $('#smartMeetingModal').classList.remove('open');
    }

    function stopSmartRecording() {
        if (smRecorder && smRecorder.state === 'recording') smRecorder.stop();
        if (smRecognition) { try { smRecognition.stop(); } catch {} smRecognition = null; }
        clearInterval(smTimerInterval);
        smRecording = false;
    }

    async function generateSmartNote() {
        $('#smRecordPhase').style.display = 'none';
        $('#smGeneratingPhase').style.display = '';
        $('#smStopBtn').style.display = 'none';
        $('#smCancelBtn').style.display = 'none';

        try {
            const fd = new FormData();
            fd.append('liveTranscript', smTranscript);
            // Attach audio blob if captured
            if (smAudioChunks.length > 0 && smRecorder) {
                const blob = new Blob(smAudioChunks, { type: smRecorder.mimeType || 'audio/webm' });
                const ext = blob.type.includes('ogg') ? '.ogg' : blob.type.includes('mp4') ? '.m4a' : '.webm';
                fd.append('audio', blob, 'meeting' + ext);
            }
            const res = await fetch('/api/notes/smart-meeting', {
                method: 'POST',
                headers: { 'RequestVerificationToken': TOKEN },
                body: fd
            });
            if (!res.ok) {
                const e = await res.json().catch(() => ({}));
                throw new Error(e.error || 'Generation failed');
            }
            const data = await res.json();
            // Show preview
            $('#smGeneratingPhase').style.display = 'none';
            $('#smPreviewPhase').style.display = '';
            $('#smNotesPreview').value = data.note.content || '';
            if (data.transcript) {
                $('#smTranscriptFull').textContent = data.transcript;
            }
            $('#smSaveBtn').style.display = '';
            $('#smCancelBtn').style.display = '';
            // Store note id for potential re-save
            $('#smSaveBtn').dataset.noteId = data.note.id;
            $('#smSaveBtn').dataset.noteJson = JSON.stringify(data.note);
        } catch (err) {
            $('#smGeneratingPhase').style.display = 'none';
            $('#smRecordPhase').style.display = '';
            $('#smCancelBtn').style.display = '';
            toast('Smart Note: ' + err.message);
        }
    }

    $('#smartMeetingBtn').addEventListener('click', e => { e.stopPropagation(); openSmartMeeting(); });
    $('#smartMeetingClose').addEventListener('click', closeSmartMeeting);
    $('#smCancelBtn').addEventListener('click', closeSmartMeeting);
    $('#smartMeetingModal').addEventListener('click', e => { if (e.target === $('#smartMeetingModal')) closeSmartMeeting(); });

    $('#smShowTranscript').addEventListener('click', function () {
        const box = $('#smTranscriptFull');
        const visible = box.style.display !== 'none';
        box.style.display = visible ? 'none' : '';
        this.innerHTML = visible
            ? '<i class="bi bi-chevron-down"></i> Show full transcript'
            : '<i class="bi bi-chevron-up"></i> Hide transcript';
    });

    $('#smStopBtn').addEventListener('click', () => {
        stopSmartRecording();
        // onstop callback will fire generateSmartNote
    });

    $('#smRecordBtn').addEventListener('click', async () => {
        if (smRecording) {
            stopSmartRecording();
            return;
        }
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            smAudioChunks = []; smTranscript = ''; smFinalTranscript = ''; smSeconds = 0;

            const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus'
                : MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm'
                : MediaRecorder.isTypeSupported('audio/ogg') ? 'audio/ogg'
                : '';
            smRecorder = new MediaRecorder(stream, mimeType ? { mimeType } : {});
            smRecorder.ondataavailable = e => { if (e.data.size > 0) smAudioChunks.push(e.data); };
            smRecorder.onstop = () => {
                stream.getTracks().forEach(t => t.stop());
                generateSmartNote();
            };
            smRecorder.start(500);
            smRecording = true;

            $('#smRecordBtn').classList.add('recording');
            $('#smRecordBtn').innerHTML = '<i class="bi bi-stop-fill"></i>';
            $('#smStopBtn').style.display = '';
            $('#smTimer').style.display = '';
            $('#smWaveWrap').style.display = '';
            $('#smHint').style.display = 'none';
            $('#smTranscriptBox').style.display = '';
            $('#smStatus').textContent = 'Recording… say "end meeting" or press End Meeting to stop.';

            smTimerInterval = setInterval(() => {
                smSeconds++;
                $('#smTimer').textContent = `${Math.floor(smSeconds / 60)}:${String(smSeconds % 60).padStart(2, '0')}`;
                if (smSeconds >= 3600) stopSmartRecording(); // 1 hour max
            }, 1000);

            // Web Speech API for live transcription (free)
            if ('webkitSpeechRecognition' in window || 'SpeechRecognition' in window) {
                const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
                smRecognition = new SR();
                smRecognition.continuous = true;
                smRecognition.interimResults = true;
                smRecognition.lang = navigator.language || 'en-US';

                smRecognition.onresult = ev => {
                    let interim = '';
                    for (let i = ev.resultIndex; i < ev.results.length; i++) {
                        if (ev.results[i].isFinal) smFinalTranscript += ev.results[i][0].transcript + ' ';
                        else interim += ev.results[i][0].transcript;
                    }
                    smTranscript = (smFinalTranscript + interim).trim();
                    const box = $('#smTranscriptText');
                    box.textContent = smTranscript;
                    box.scrollTop = box.scrollHeight;

                    // Auto-detect "end meeting"
                    const lower = smTranscript.toLowerCase();
                    if (lower.includes('end meeting') || lower.includes('end the meeting') || lower.includes('stop meeting')) {
                        // Remove the trigger phrase from transcript
                        smFinalTranscript = smFinalTranscript.replace(/end (the )?meeting\.?/gi, '').trim();
                        smTranscript = smFinalTranscript;
                        stopSmartRecording();
                    }
                };
                smRecognition.onerror = () => {};
                smRecognition.onend = () => {
                    // Restart if still recording (Web Speech API auto-stops after silence)
                    if (smRecording && smRecognition) {
                        try { smRecognition.start(); } catch {}
                    }
                };
                try { smRecognition.start(); } catch {}
            }
        } catch (err) {
            toast('Microphone access denied. Please allow microphone in browser settings.');
        }
    });

    $('#smSaveBtn').addEventListener('click', async () => {
        const btn = $('#smSaveBtn');
        const noteId = btn.dataset.noteId;
        const editedContent = $('#smNotesPreview').value.trim();
        btn.disabled = true;
        btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Saving…';
        try {
            // Update note content if user edited the preview
            const noteData = JSON.parse(btn.dataset.noteJson || '{}');
            if (editedContent !== noteData.content) {
                await api('PUT', `/api/notes/${noteId}`, {
                    title: noteData.title,
                    content: editedContent,
                    colorHex: noteData.colorHex || '#ffffff',
                    labels: noteData.labels || null
                });
                noteData.content = editedContent;
            }
            noteData.content = editedContent;
            addNoteToDom(noteData, true);
            $('#notesEmpty').style.display = 'none';
            closeSmartMeeting();
            toast('Meeting notes saved ✓');
            if (currentTab !== 'notes') activateTab('notes');
        } catch (e) {
            toast(e.message);
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-check-lg"></i> Save Note';
        }
    });

    const fabSmart = $('#fabSmart');
    if (fabSmart) {
        fabSmart.addEventListener('click', e => {
            e.stopPropagation();
            toggleFab(false);
            openSmartMeeting();
        });
    }

    // ---------- Search ----------
    $('#noteSearch').addEventListener('input', e => {
        const q = e.target.value.toLowerCase();
        $$('.note').forEach(c => {
            const txt = (c.textContent + ' ' + (c.dataset.labels || '')).toLowerCase();
            c.style.display = txt.includes(q) ? '' : 'none';
        });
    });

    // ---------- Quick Log ----------
    // Parses natural language like "2h meeting with Acme re: Q3" into task fields
    const QL_CATS = { development:0,dev:0,coding:0,code:0,build:0,deploy:0,fix:0,bug:0,implement:0,refactor:0,test:0,testing:0, meeting:1,meet:1,standup:1,call:1,sync:1,discussion:1,discuss:1, support:2,ticket:2,customer:2,help:2,issue:2, review:3,pr:3,'code review':3,feedback:3, planning:4,plan:4,sprint:4,roadmap:4,estimate:4,backlog:4, research:5,spike:5,investigate:5,explore:5, documentation:6,docs:6,doc:6,write:6,readme:6,spec:6, other:7 };

    function parseQuickLog(text) {
        text = text.trim();
        if (!text) return null;
        let hours = 1, project = null, task = text;

        // Extract hours: "2h", "1.5h", "45min", "30m", "2 hours"
        const hrMatch = text.match(/(\d+(?:\.\d+)?)\s*h(?:ours?)?/i);
        const minMatch = text.match(/(\d+)\s*m(?:in(?:utes?)?)?(?:\b|$)/i);
        if (hrMatch) { hours = parseFloat(hrMatch[1]); task = task.replace(hrMatch[0], '').trim(); }
        else if (minMatch) { hours = Math.round(parseFloat(minMatch[1]) / 15) * 0.25 || 0.25; task = task.replace(minMatch[0], '').trim(); }

        // Extract project after "for", "on", "re:", "with" — heuristic
        const projMatch = task.match(/\b(?:for|with|re:?)\s+([A-Z][A-Za-z0-9 &'-]{1,30}?)(?:\s+(?:re|about|regarding|on|for)\b|$)/);
        if (projMatch) project = projMatch[1].trim();

        // Clean leading/trailing punctuation
        task = task.replace(/^[-–—:,]+\s*/, '').replace(/\s*[-–—:,]+$/, '').trim();
        if (!task) task = text.trim();

        // Infer category from keywords
        const lower = task.toLowerCase();
        let category = 7;
        for (const [kw, cat] of Object.entries(QL_CATS)) {
            if (lower.includes(kw)) { category = cat; break; }
        }

        return { task, hours: Math.max(0.25, Math.min(24, hours)), category, project, date: RANGE.today };
    }

    let qlParsed = null;

    function showQlPreview(parsed) {
        qlParsed = parsed;
        $('#qlTask').textContent = parsed.task;
        $('#qlCat').textContent = CAT[parsed.category];
        $('#qlHours').textContent = parsed.hours + 'h';
        if (parsed.project) { $('#qlProj').textContent = parsed.project; $('#qlProj').style.display = ''; }
        else $('#qlProj').style.display = 'none';
        $('#qlPreview').style.display = '';
    }

    async function submitQuickLog(parsed) {
        const btn = $('#qlConfirm');
        btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Saving…';
        try {
            const w = await api('POST', '/api/work', {
                task: parsed.task, project: parsed.project || null,
                category: parsed.category, status: 2,
                hours: parsed.hours, date: parsed.date,
                billable: true, notes: null
            });
            if (inRange(w.date)) { const tr = document.createElement('tr'); setRowData(tr, w); $('#taskBody').prepend(tr); }
            $('#quickLogInput').value = '';
            $('#qlPreview').style.display = 'none';
            qlParsed = null;
            taskEmptyCheck(); toast('⚡ Task logged!');
        } catch (e) { toast(e.message); }
        finally { btn.disabled = false; btn.innerHTML = '<i class="bi bi-check-lg"></i> Confirm & Save'; }
    }

    $('#quickLogBtn').addEventListener('click', () => {
        const val = $('#quickLogInput').value.trim();
        if (!val) return;
        const parsed = parseQuickLog(val);
        if (parsed) showQlPreview(parsed);
    });
    $('#quickLogInput').addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            const val = $('#quickLogInput').value.trim();
            if (!val) return;
            if (qlParsed) { submitQuickLog(qlParsed); return; }
            const parsed = parseQuickLog(val);
            if (parsed) showQlPreview(parsed);
        }
        if (e.key === 'Escape') { $('#qlPreview').style.display = 'none'; qlParsed = null; }
    });
    $('#qlConfirm').addEventListener('click', () => { if (qlParsed) submitQuickLog(qlParsed); });
    $('#qlEdit').addEventListener('click', () => {
        if (!qlParsed) return;
        openTaskModal(null);
        $('#tTask').value = qlParsed.task;
        $('#tHours').value = qlParsed.hours;
        $('#tCategory').value = qlParsed.category;
        if (qlParsed.project) $('#tProject').value = qlParsed.project;
        $('#qlPreview').style.display = 'none';
        qlParsed = null;
    });

    // ---------- Export CSV ----------
    $('#exportCsvBtn').addEventListener('click', () => {
        const rows = $$('#taskBody tr[data-id]');
        if (!rows.length) return toast('No tasks to export');
        const headers = ['Date','Task','Project','Category','Status','Hours','Billable'];
        const lines = [headers.join(',')];
        rows.forEach(r => {
            lines.push([
                r.dataset.date,
                `"${(r.dataset.task || '').replace(/"/g,'""')}"`,
                `"${(r.dataset.project || '').replace(/"/g,'""')}"`,
                CAT[+r.dataset.category] || '',
                STAT[+r.dataset.status] || '',
                r.dataset.hours,
                r.dataset.billable === 'true' ? 'Yes' : 'No'
            ].join(','));
        });
        const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `timesheet-${RANGE.from}-to-${RANGE.to}.csv`;
        a.click(); URL.revokeObjectURL(a.href);
        toast('CSV exported');
    });

    // ---------- Import CSV ----------
    $('#importCsvBtn').addEventListener('click', () => $('#csvFileInput').click());
    $('#csvFileInput').addEventListener('change', async e => {
        const file = e.target.files[0]; if (!file) return;
        e.target.value = '';
        const text = await file.text();
        const lines = text.trim().split('\n').map(l => l.trim()).filter(Boolean);
        if (lines.length < 2) return toast('CSV is empty or has no data rows');
        // Parse header to find column indexes
        const header = lines[0].split(',').map(h => h.replace(/"/g,'').trim().toLowerCase());
        const idx = k => header.findIndex(h => h.includes(k));
        const iDate = idx('date'), iTask = idx('task'), iProj = idx('proj'),
              iCat = idx('cat'), iStat = idx('stat'), iHours = idx('hour'), iBill = idx('bill');
        if (iTask < 0 || iDate < 0) return toast('CSV must have Date and Task columns');

        let saved = 0, failed = 0;
        for (let i = 1; i < lines.length; i++) {
            // Handle quoted fields
            const cols = lines[i].match(/(".*?"|[^,]+|(?<=,)(?=,)|^(?=,))/g) || [];
            const col = j => j < 0 ? '' : (cols[j] || '').replace(/^"|"$/g,'').trim();
            const catName = col(iCat).toLowerCase();
            const catIdx = Object.entries(QL_CATS).find(([k]) => catName.includes(k));
            const statName = col(iStat).toLowerCase();
            const statIdx = STAT.findIndex(s => s.toLowerCase() === statName);
            try {
                const w = await api('POST', '/api/work', {
                    task: col(iTask) || 'Imported task',
                    project: col(iProj) || null,
                    category: catIdx ? catIdx[1] : 0,
                    status: statIdx >= 0 ? statIdx : 2,
                    hours: parseFloat(col(iHours)) || 1,
                    date: col(iDate) || RANGE.today,
                    billable: col(iBill).toLowerCase() !== 'no',
                    notes: null
                });
                if (inRange(w.date)) { const tr = document.createElement('tr'); setRowData(tr, w); $('#taskBody').append(tr); }
                saved++;
            } catch { failed++; }
        }
        taskEmptyCheck();
        toast(`Imported ${saved} task${saved !== 1 ? 's' : ''}${failed ? ` (${failed} failed)` : ''}`);
    });

    // ---------- Print Timesheet ----------
    $('#printTimesheetBtn').addEventListener('click', () => {
        const rows = $$('#taskBody tr[data-id]');
        const weekLabel = document.querySelector('.weeknav strong')?.textContent || '';
        const totalHours = rows.reduce((s, r) => s + (+r.dataset.hours || 0), 0);
        const billHours = rows.filter(r => r.dataset.billable === 'true').reduce((s, r) => s + (+r.dataset.hours || 0), 0);

        const tableRows = rows.map(r => `
            <tr>
                <td>${r.dataset.date}</td>
                <td>${esc(r.dataset.task)}</td>
                <td>${esc(r.dataset.project) || '—'}</td>
                <td>${CAT[+r.dataset.category] || ''}</td>
                <td>${STAT[+r.dataset.status] || ''}</td>
                <td style="text-align:right">${r.dataset.hours}h</td>
                <td style="text-align:center">${r.dataset.billable === 'true' ? '✓' : ''}</td>
            </tr>`).join('');

        const html = `<!DOCTYPE html><html><head><meta charset="UTF-8">
        <title>Timesheet — ${weekLabel}</title>
        <style>
            body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;margin:32px;color:#1a1a1a}
            h1{font-size:22px;margin:0 0 4px}
            .sub{color:#64748b;font-size:13px;margin-bottom:24px}
            table{width:100%;border-collapse:collapse;font-size:13px}
            th{text-align:left;padding:8px 10px;background:#f8f9fa;border-bottom:2px solid #e2e8f0;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:.04em}
            td{padding:8px 10px;border-bottom:1px solid #f1f5f9}
            tr:last-child td{border-bottom:none}
            .totals{margin-top:20px;text-align:right;font-size:13px;color:#64748b}
            .totals strong{color:#1a1a1a;font-size:15px}
            @media print{body{margin:16px}}
        </style></head><body>
        <h1>Timesheet</h1>
        <div class="sub">${weekLabel} &nbsp;·&nbsp; Generated ${new Date().toLocaleDateString()}</div>
        <table>
            <thead><tr><th>Date</th><th>Task</th><th>Project</th><th>Category</th><th>Status</th><th style="text-align:right">Hours</th><th style="text-align:center">Billable</th></tr></thead>
            <tbody>${tableRows}</tbody>
        </table>
        <div class="totals">Total: <strong>${totalHours.toFixed(1)}h</strong> &nbsp;·&nbsp; Billable: <strong>${billHours.toFixed(1)}h</strong></div>
        <script>window.onload=()=>window.print()<\/script>
        </body></html>`;

        const w = window.open('', '_blank');
        w.document.write(html);
        w.document.close();
    });

    // ---------- Tasks ----------
    const taskModal = $('#taskModal');
    function openTaskModal(row) {
        $('#taskModalTitle').textContent = row ? 'Edit task' : 'Log a task';
        $('#tId').value = row ? row.dataset.id : '';
        $('#tTask').value = row ? row.dataset.task : '';
        $('#tProject').value = row ? (row.dataset.project || '') : '';
        $('#tDate').value = row ? row.dataset.date : RANGE.today;
        $('#tCategory').value = row ? row.dataset.category : '0';
        $('#tStatus').value = row ? row.dataset.status : '2';
        $('#tHours').value = row ? row.dataset.hours : '1';
        $('#tBillable').checked = row ? row.dataset.billable === 'true' : true;
        taskModal.classList.add('open');
    }
    $('#addTaskBtn').addEventListener('click', () => openTaskModal(null));

    // ── Inline row editor ─────────────────────────────────────────────────────
    function catOptions(sel) {
        return CAT.map((c, i) => `<option value="${i}"${sel == i ? ' selected' : ''}>${c}</option>`).join('');
    }
    function statOptions(sel) {
        return STAT.map((s, i) => `<option value="${i}"${sel == i ? ' selected' : ''}>${s}</option>`).join('');
    }
    function rowEditHtml(w) {
        return `<td colspan="7" class="task-editing-cell">
          <div class="task-inline-edit">
            <input class="input ie-task" value="${esc(w.task || '')}" placeholder="What did you work on?" />
            <div class="ie-row">
              <input class="input ie-project" value="${esc(w.project || '')}" placeholder="Project (optional)" />
              <input class="input ie-date" type="date" value="${(w.date || RANGE.today).slice(0, 10)}" />
              <select class="input ie-cat">${catOptions(w.category)}</select>
              <select class="input ie-status">${statOptions(w.status)}</select>
              <input class="input ie-hours" type="number" value="${w.hours || 1}" min="0.25" step="0.25" />
              <label class="ie-bill"><input type="checkbox" class="ie-billable"${w.billable ? ' checked' : ''} /> Billable</label>
            </div>
            <div class="ie-actions">
              <button class="btn btn-ghost btn-sm ie-cancel" type="button">Cancel</button>
              <button class="btn btn-amber btn-sm ie-save" type="button"><i class="bi bi-check-lg"></i> Save</button>
            </div>
          </div>
        </td>`;
    }

    function openInlineEdit(row) {
        // close any other open inline editor
        $$('#taskBody tr.task-editing').forEach(r => { if (r !== row) cancelInlineEdit(r); });
        if (row.classList.contains('task-editing')) return;
        row.classList.add('task-editing');
        const w = {
            task: row.dataset.task, project: row.dataset.project,
            date: row.dataset.date, category: +row.dataset.category,
            status: +row.dataset.status, hours: +row.dataset.hours,
            billable: row.dataset.billable === 'true'
        };
        row._savedCells = row.innerHTML;
        row.innerHTML = rowEditHtml(w);
        row.querySelector('.ie-task').focus();
    }

    function cancelInlineEdit(row) {
        if (!row.classList.contains('task-editing')) return;
        row.classList.remove('task-editing');
        row.innerHTML = row._savedCells;
    }

    async function saveInlineEdit(row) {
        if (!row.classList.contains('task-editing')) return;
        const task = row.querySelector('.ie-task').value.trim();
        if (!task) return toast('Task description is required');
        const payload = {
            task,
            project: row.querySelector('.ie-project').value.trim(),
            date: row.querySelector('.ie-date').value,
            category: +row.querySelector('.ie-cat').value,
            status: +row.querySelector('.ie-status').value,
            hours: +row.querySelector('.ie-hours').value || 1,
            billable: row.querySelector('.ie-billable').checked,
            notes: null
        };
        try {
            const w = await api('PUT', `/api/work/${row.dataset.id}`, payload);
            row.classList.remove('task-editing');
            if (inRange(w.date)) { setRowData(row, w); }
            else { row.remove(); }
            taskEmptyCheck(); toast('Task saved');
        } catch (err) { toast(err.message); }
    }

    $('#taskBody').addEventListener('click', async e => {
        const row = e.target.closest('tr'); if (!row) return;

        if (e.target.closest('.ie-save')) { saveInlineEdit(row); return; }
        if (e.target.closest('.ie-cancel')) { cancelInlineEdit(row); return; }

        if (e.target.closest('.editTask')) { openInlineEdit(row); return; }
        if (e.target.closest('.delTask')) {
            if (!confirm('Delete this task?')) return;
            try { await api('DELETE', `/api/work/${row.dataset.id}`); row.remove(); taskEmptyCheck(); toast('Deleted'); }
            catch (err) { toast(err.message); }
        }
    });

    // Enter = save, Escape = cancel in inline editor
    $('#taskBody').addEventListener('keydown', e => {
        const row = e.target.closest('tr.task-editing');
        if (!row) return;
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); saveInlineEdit(row); }
        if (e.key === 'Escape') cancelInlineEdit(row);
    });
    function rowHtml(w) {
        return `<td>${fmtDate(w.date)}</td>
            <td><strong>${esc(w.task)}</strong>${w.billable ? '' : ' <span class="pill p-other" style="font-size:10px">non-billable</span>'}</td>
            <td class="hide-mobile">${esc(w.project) || '—'}</td>
            <td class="hide-mobile"><span class="pill ${catCls(w.category)}">${w.categoryName}</span></td>
            <td><span class="pill ${statCls(w.status)}">${w.statusName}</span></td>
            <td><strong>${(+w.hours).toFixed(1).replace(/\.0$/, '')}h</strong></td>
            <td style="text-align:right;white-space:nowrap">
                <button class="mini-btn editTask" type="button"><i class="bi bi-pencil"></i></button>
                <button class="mini-btn delTask" type="button"><i class="bi bi-trash"></i></button></td>`;
    }
    function setRowData(tr, w) {
        tr.dataset.id = w.id; tr.dataset.task = w.task; tr.dataset.project = w.project || '';
        tr.dataset.category = w.category; tr.dataset.status = w.status; tr.dataset.hours = w.hours;
        tr.dataset.date = w.date; tr.dataset.billable = w.billable; tr.dataset.notes = w.notes || '';
        tr.innerHTML = rowHtml(w);
    }
    function fmtDate(d) { const dt = new Date(d + 'T00:00:00'); return dt.toLocaleDateString('en-US', { weekday: 'short', day: '2-digit' }); }
    function inRange(d) { return d >= RANGE.from && d <= RANGE.to; }
    function taskEmptyCheck() { $('#tasksEmpty').style.display = $('#taskBody').children.length ? 'none' : ''; }

    // Smart AI: suggest category + hours from task description
    $('#aiSuggestBtn').addEventListener('click', async () => {
        const task = $('#tTask').value.trim();
        if (!task) return toast('Enter a task description first');
        const btn = $('#aiSuggestBtn'); btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i>';
        try {
            const r = await api('POST', '/api/work/suggest', { task });
            $('#tCategory').value = r.category;
            $('#tHours').value = r.hours;
            toast('Smart AI suggested category & hours');
        } catch (e) { toast(e.message); }
        finally { btn.disabled = false; btn.innerHTML = '<i class="bi bi-cpu"></i> Suggest'; }
    });

    $('#saveTaskBtn').addEventListener('click', async () => {
        const task = $('#tTask').value.trim();
        if (!task) return toast('Task is required');
        const payload = {
            task, project: $('#tProject').value, category: +$('#tCategory').value, status: +$('#tStatus').value,
            hours: +$('#tHours').value, date: $('#tDate').value, billable: $('#tBillable').checked, notes: null
        };
        const id = $('#tId').value;
        try {
            if (id) {
                const w = await api('PUT', `/api/work/${id}`, payload);
                const tr = $(`#taskBody tr[data-id="${id}"]`);
                if (inRange(w.date)) { setRowData(tr, w); } else if (tr) { tr.remove(); }
            } else {
                const w = await api('POST', '/api/work', payload);
                if (inRange(w.date)) { const tr = document.createElement('tr'); setRowData(tr, w); $('#taskBody').append(tr); }
                else { toast('Saved to ' + w.date + ' (other week)'); }
            }
            taskModal.classList.remove('open'); taskEmptyCheck(); toast('Task saved');
        } catch (e) { toast(e.message); }
    });

    // ---------- Smart AI: Daily Standup ----------
    const standupModal = $('#standupModal');

    async function generateStandup() {
        const out = $('#standupOut'), src = $('#standupSource');
        out.textContent = ''; src.textContent = '';
        out.innerHTML = '<div style="text-align:center;padding:16px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:22px;display:block;margin-bottom:6px"></i>Smart AI is writing your standup…</div>';
        try {
            const r = await api('GET', '/api/work/standup');
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong>`;
        } catch (e) { out.textContent = '⚠ ' + e.message; }
    }

    $('#standupBtn').addEventListener('click', () => {
        standupModal.classList.add('open');
        generateStandup();
    });
    $('#regenStandupBtn').addEventListener('click', generateStandup);
    $('#copyStandupBtn').addEventListener('click', () => {
        navigator.clipboard.writeText($('#standupOut').textContent);
        toast('Standup copied to clipboard');
    });

    // ---------- AI weekly summary ----------
    $('#genSummaryBtn').addEventListener('click', async () => {
        const btn = $('#genSummaryBtn'); const out = $('#aiOut'); const src = $('#aiSource');
        btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Thinking…';
        out.textContent = ''; src.textContent = '';
        try {
            const r = await api('GET', `/api/work/summary?from=${RANGE.from}&to=${RANGE.to}`);
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong> · ${r.count} entries · ${r.hours}h`;
            $('#aiCopyWrap').style.display = '';
        } catch (e) { out.textContent = '⚠ ' + e.message; }
        finally { btn.disabled = false; btn.innerHTML = '<i class="bi bi-stars"></i> Generate summary'; }
    });
    $('#copySummaryBtn').addEventListener('click', () => { navigator.clipboard.writeText($('#aiOut').textContent); toast('Copied'); });

    // ---------- Reports ----------
    let reportsLoaded = false;
    async function loadReports() {
        if (reportsLoaded) return; reportsLoaded = true;
        try {
            const r = await api('GET', `/api/work/report?from=${RANGE.from}&to=${RANGE.to}`);
            renderBars($('#repCategory'), r.byCategory.map(x => ({ k: x.category, v: x.hours })));
            renderBars($('#repProject'), r.byProject.map(x => ({ k: x.project, v: x.hours })));
            renderBars($('#repDay'), r.byDay.map(x => ({ k: fmtDate(x.date), v: x.hours })));
        } catch (e) { toast(e.message); }
    }
    function renderBars(host, items) {
        if (!items.length) { host.innerHTML = '<div class="empty" style="padding:28px"><p>No data for this period.</p></div>'; return; }
        const max = Math.max(...items.map(i => i.v), 0.001);
        host.innerHTML = items.map(i => `
            <div style="margin-bottom:13px">
              <div style="display:flex;justify-content:space-between;font-size:13.5px;margin-bottom:5px"><span>${esc(i.k)}</span><strong>${(+i.v).toFixed(1).replace(/\.0$/,'')}h</strong></div>
              <div class="bar"><span style="width:${Math.round(i.v / max * 100)}%"></span></div>
            </div>`).join('');
    }

    // ---------- Live sync from sticky notes ----------
    let lastSyncAt = new Date().toISOString();
    async function syncNotes() {
        const activeTab = $$('.app-tab').find(b => b.classList.contains('active'));
        if (!activeTab || activeTab.dataset.tab !== 'notes') return;
        try {
            const updated = await api('GET', `/api/notes?since=${encodeURIComponent(lastSyncAt)}`);
            if (!updated.length) return;
            lastSyncAt = new Date().toISOString();
            updated.forEach(n => {
                const existing = $(`[data-id="${n.id}"]`);
                if (existing) {
                    const newEl = makeNoteEl(n);
                    existing.replaceWith(newEl);
                } else {
                    addNoteToDom(n, false);
                    $('#notesEmpty').style.display = 'none';
                }
            });
            refreshSections();
        } catch { /* ignore network errors */ }
    }
    setInterval(syncNotes, 5000);

    // ---------- Smart AI: Weekly Retro ----------
    const retroModal = $('#retroModal');

    async function generateRetro() {
        const out = $('#retroOut'), src = $('#retroSource');
        out.innerHTML = '<div style="text-align:center;padding:16px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:22px;display:block;margin-bottom:6px"></i>Smart AI is writing your retro…</div>';
        src.textContent = '';
        try {
            const r = await api('GET', `/api/work/retro?from=${RANGE.from}&to=${RANGE.to}`);
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong>`;
        } catch (e) { out.textContent = '⚠ ' + e.message; }
    }

    $('#retroBtn').addEventListener('click', () => { retroModal.classList.add('open'); generateRetro(); });
    $('#regenRetroBtn').addEventListener('click', generateRetro);
    $('#copyRetroBtn').addEventListener('click', () => { navigator.clipboard.writeText($('#retroOut').textContent); toast('Retro copied!'); });

    // ---------- Smart AI: Productivity Insights ----------
    const productivityModal = $('#productivityModal');

    async function generateProductivity() {
        const out = $('#productivityOut'), src = $('#productivitySource');
        out.innerHTML = '<div style="text-align:center;padding:16px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:22px;display:block;margin-bottom:6px"></i>Analyzing your work patterns…</div>';
        src.textContent = '';
        try {
            const r = await api('GET', `/api/work/productivity?from=${RANGE.from}&to=${RANGE.to}`);
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong>`;
        } catch (e) { out.textContent = '⚠ ' + e.message; }
    }

    $('#productivityBtn').addEventListener('click', () => { productivityModal.classList.add('open'); generateProductivity(); });
    $('#regenProductivityBtn').addEventListener('click', generateProductivity);

    // ---------- Smart AI: Status Update ----------
    const statusUpdateModal = $('#statusUpdateModal');
    let statusFmt = 'email';

    async function generateStatusUpdate() {
        const out = $('#statusUpdateOut'), src = $('#statusUpdateSource');
        out.innerHTML = '<div style="text-align:center;padding:16px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:22px;display:block;margin-bottom:6px"></i>Drafting your status update…</div>';
        src.textContent = '';
        try {
            const r = await api('GET', `/api/work/status-update?from=${RANGE.from}&to=${RANGE.to}&format=${statusFmt}`);
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong>`;
        } catch (e) { out.textContent = '⚠ ' + e.message; }
    }

    $('#statusUpdateBtn').addEventListener('click', () => { statusUpdateModal.classList.add('open'); generateStatusUpdate(); });
    $('#statusFmtEmail').addEventListener('click', () => { statusFmt = 'email'; $('#statusFmtEmail').className = 'btn btn-amber btn-sm'; $('#statusFmtSlack').className = 'btn btn-ghost btn-sm'; generateStatusUpdate(); });
    $('#statusFmtSlack').addEventListener('click', () => { statusFmt = 'slack'; $('#statusFmtSlack').className = 'btn btn-amber btn-sm'; $('#statusFmtEmail').className = 'btn btn-ghost btn-sm'; generateStatusUpdate(); });
    $('#copyStatusBtn').addEventListener('click', () => { navigator.clipboard.writeText($('#statusUpdateOut').textContent); toast('Status update copied!'); });

    // ---------- Smart AI: Ask Notes ----------
    const askAiModal = $('#askAiModal');

    async function askAi() {
        const q = $('#askAiInput').value.trim();
        if (!q) return toast('Enter a question first');
        const out = $('#askAiOut'), src = $('#askAiSource');
        out.innerHTML = '<div style="text-align:center;padding:12px;color:var(--muted)"><i class="bi bi-cpu" style="font-size:20px;display:block;margin-bottom:4px"></i>Searching your notes…</div>';
        src.textContent = '';
        try {
            const r = await api('POST', '/api/notes/ask', { question: q });
            out.textContent = r.answer;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>Smart AI</strong>`;
        } catch (e) { out.textContent = '⚠ ' + e.message; }
    }

    $('#askAiBtn').addEventListener('click', () => { askAiModal.classList.add('open'); $('#askAiInput').focus(); });
    $('#askAiSubmitBtn').addEventListener('click', askAi);
    $('#askAiInput').addEventListener('keydown', e => { if (e.key === 'Enter') askAi(); });

    // ---------- Friday nudge banner ----------
    (function () {
        const banner = $('#fridayNudge');
        if (!banner) return;
        const dismissed = localStorage.getItem('wt_nudge_dismissed');
        const today = new Date();
        const isFriday = today.getDay() === 5;
        const thisWeekKey = `friday_${today.getFullYear()}_${Math.floor((today - new Date(today.getFullYear(), 0, 1)) / 604800000)}`;
        if (isFriday && dismissed !== thisWeekKey) banner.style.display = 'flex';

        $('#nudgeSummaryBtn').addEventListener('click', () => {
            banner.style.display = 'none';
            localStorage.setItem('wt_nudge_dismissed', thisWeekKey);
            activateTab('timesheet');
            setTimeout(() => { const btn = $('#genSummaryBtn'); if (btn) btn.click(); }, 200);
        });
        $('#nudgeDismissBtn').addEventListener('click', () => {
            banner.style.display = 'none';
            localStorage.setItem('wt_nudge_dismissed', thisWeekKey);
        });
    })();

    // ---------- End-of-day quick log ----------
    (function () {
        const eodKey = 'wt_eod_' + new Date().toISOString().slice(0, 10);
        if (localStorage.getItem(eodKey)) return; // already shown today

        const hour = new Date().getHours();
        if (hour < 16) return; // only show after 4pm

        const modal = $('#eodModal');
        setTimeout(() => modal && modal.classList.add('open'), 1500);

        // Mark as seen whenever modal is dismissed (save or skip)
        const markSeen = () => localStorage.setItem(eodKey, '1');

        $('#saveEodBtn').addEventListener('click', async () => {
            const task = $('#eodTask').value.trim();
            if (!task) return toast('Add at least a task description');
            try {
                const today = new Date().toISOString().slice(0, 10);
                await api('POST', '/api/work', {
                    task,
                    project: $('#eodProject').value.trim() || null,
                    hours: parseFloat($('#eodHours').value) || 8,
                    date: today,
                    category: 0,
                    status: 2,
                    billable: true
                });
                markSeen();
                modal.classList.remove('open');
                toast('Logged! Great work today 🎉');
                setTimeout(() => location.reload(), 800);
            } catch (e) { toast('⚠ ' + e.message); }
        });

        // Skip today — mark seen so it won't reappear; global data-close closes the modal
        $$('[data-close]', modal).forEach(b => b.addEventListener('click', markSeen));
    })();

    // ---------- Settings modal ----------
    const settingsModal = $('#settingsModal');
    $('#settingsBtn').addEventListener('click', () => settingsModal.classList.add('open'));
    $('#saveSettingsBtn').addEventListener('click', async () => {
        try {
            await api('POST', '/api/user/settings', {
                emailDigestEnabled: $('#sDigest').checked,
                hourlyRate: parseFloat($('#sRate').value) || 0,
                jobTitle: $('#sTitle').value.trim(),
                company: $('#sCompany').value.trim(),
                fullName: ($('#sName') ? $('#sName').value.trim() : '')
            });
            settingsModal.classList.remove('open');
            toast('Settings saved!');
            setTimeout(() => location.reload(), 600);
        } catch (e) { toast('⚠ ' + e.message); }
    });

    // ---------- Share link ----------
    (function () {
        const btn = $('#shareLinkBtn');
        const modal = $('#shareLinkModal');
        if (!btn || !modal) return;

        btn.addEventListener('click', () => {
            const userId = body.dataset.userId;
            const weekOffset = parseInt(body.dataset.weekOffset || '0', 10);
            const url = `${location.origin}/share/${userId}${weekOffset !== 0 ? '?week=' + weekOffset : ''}`;
            $('#shareLinkUrl').value = url;
            $('#openShareLinkBtn').href = url;
            modal.classList.add('open');
        });

        $('#copyShareLinkBtn').addEventListener('click', async () => {
            const url = $('#shareLinkUrl').value;
            try { await navigator.clipboard.writeText(url); toast('Link copied — share it anywhere!'); }
            catch { toast('Copy failed — select and copy the link manually.'); }
        });
    })();

    // ---------- Email manager ----------
    (function () {
        const btn = $('#emailManagerBtn');
        const modal = $('#emailManagerModal');
        const sendBtn = $('#sendEmailManagerBtn');
        if (!btn || !modal || !sendBtn) return;

        btn.addEventListener('click', () => modal.classList.add('open'));

        sendBtn.addEventListener('click', async () => {
            const email = $('#emEmail').value.trim();
            if (!email) return toast('Enter your manager\'s email');
            const orig = sendBtn.innerHTML;
            sendBtn.disabled = true; sendBtn.innerHTML = '<i class="bi bi-hourglass-split"></i> Sending…';
            try {
                await api('POST', '/api/work/email-manager', {
                    managerEmail: email,
                    message: $('#emMessage').value.trim() || null,
                    from: RANGE.from,
                    to: RANGE.to
                });
                modal.classList.remove('open');
                toast('Report sent to ' + email + ' ✓');
            } catch (e) { toast('⚠ ' + e.message); }
            finally { sendBtn.disabled = false; sendBtn.innerHTML = orig; }
        });
    })();

    // ---------- Badge ----------
    (function () {
        const btn = $('#badgeBtn');
        const modal = $('#badgeModal');
        if (!btn || !modal) return;

        const origin = location.origin;
        const badgeUrl = `${origin}/badge.svg`;
        const siteUrl = origin;

        btn.addEventListener('click', () => {
            $('#badgeHtml').value = `<a href="${siteUrl}" title="I track my work on worklog.today"><img src="${badgeUrl}" alt="tracked with worklog.today" height="20" /></a>`;
            $('#badgeMd').value = `[![tracked with worklog.today](${badgeUrl})](${siteUrl})`;
            modal.classList.add('open');
        });

        $('#copyBadgeHtmlBtn').addEventListener('click', async () => {
            try { await navigator.clipboard.writeText($('#badgeHtml').value); toast('HTML badge copied!'); }
            catch { toast('Copy failed.'); }
        });
        $('#copyBadgeMdBtn').addEventListener('click', async () => {
            try { await navigator.clipboard.writeText($('#badgeMd').value); toast('Markdown badge copied!'); }
            catch { toast('Copy failed.'); }
        });
    })();

    // ---------- Share card ----------
    (function () {
        const shareBtn = $('#shareCardBtn');
        const shareModal = $('#shareCardModal');
        const canvas = $('#shareCanvas');
        if (!shareBtn || !shareModal || !canvas) return;

        function buildCard() {
            const sg = $('.stat-grid');
            const totalHours  = sg ? sg.dataset.totalHours    : '0';
            const billable    = sg ? sg.dataset.billableHours  : '0';
            const taskCount   = sg ? sg.dataset.taskCount      : '0';
            const earnings    = sg ? sg.dataset.earnings        : '';
            const weekLabel   = sg ? sg.dataset.weekLabel       : '';
            const userName    = ($('.user-name-wrap div') || {}).textContent?.trim() || '';
            const jobTitle    = ($$('.user-name-wrap div')[1] || {}).textContent?.trim() || '';

            const W = 1200, H = 628;
            canvas.width  = W;
            canvas.height = H;
            const ctx = canvas.getContext('2d');

            // ── Background ──────────────────────────────────────────────────
            const bg = ctx.createLinearGradient(0, 0, W, H);
            bg.addColorStop(0, '#0f172a');
            bg.addColorStop(1, '#1e293b');
            ctx.fillStyle = bg;
            ctx.fillRect(0, 0, W, H);

            // Subtle dot grid overlay
            ctx.fillStyle = 'rgba(255,255,255,0.025)';
            for (let x = 40; x < W; x += 48) {
                for (let y = 40; y < H; y += 48) {
                    ctx.beginPath(); ctx.arc(x, y, 1.5, 0, Math.PI * 2); ctx.fill();
                }
            }

            // ── Amber top bar ────────────────────────────────────────────────
            ctx.fillStyle = '#f59e0b';
            ctx.fillRect(0, 0, W, 8);

            // ── Logo (top-left) ──────────────────────────────────────────────
            const FONT = '-apple-system, BlinkMacSystemFont, "Segoe UI", Arial, sans-serif';
            ctx.font = `bold 28px ${FONT}`;
            ctx.fillStyle = '#f59e0b';
            ctx.textBaseline = 'middle';
            const logoW = ctx.measureText('worklog').width;
            ctx.fillText('worklog', 60, 66);
            ctx.fillStyle = 'rgba(255,255,255,0.45)';
            ctx.fillText('.today', 60 + logoW, 66);

            // ── Week label (top-right) ────────────────────────────────────────
            ctx.font = `18px ${FONT}`;
            ctx.fillStyle = 'rgba(255,255,255,0.45)';
            ctx.textAlign = 'right';
            ctx.fillText(weekLabel, W - 60, 66);
            ctx.textAlign = 'left';

            // ── Column separators ────────────────────────────────────────────
            ctx.strokeStyle = 'rgba(255,255,255,0.08)';
            ctx.lineWidth = 1;
            [W / 3, (W / 3) * 2].forEach(x => {
                ctx.beginPath(); ctx.moveTo(x, 130); ctx.lineTo(x, 370); ctx.stroke();
            });

            // ── Stats (3 columns) ────────────────────────────────────────────
            const stats = [
                { value: totalHours + 'h', label: 'Total hours' },
                { value: taskCount,        label: 'Tasks logged' },
                { value: billable + 'h',   label: 'Billable hours', sub: earnings || null }
            ];
            const colW = W / 3;
            ctx.textAlign = 'center';
            stats.forEach((s, i) => {
                const cx = colW * i + colW / 2;

                // Big value
                ctx.font = `bold 86px ${FONT}`;
                ctx.fillStyle = '#ffffff';
                ctx.fillText(s.value, cx, 258);

                // Label
                ctx.font = `21px ${FONT}`;
                ctx.fillStyle = 'rgba(255,255,255,0.5)';
                ctx.fillText(s.label, cx, 316);

                // Sub-label (earnings)
                if (s.sub) {
                    ctx.font = `bold 20px ${FONT}`;
                    ctx.fillStyle = '#f59e0b';
                    ctx.fillText(s.sub + ' earned', cx, 348);
                }
            });
            ctx.textAlign = 'left';

            // ── Divider ───────────────────────────────────────────────────────
            ctx.strokeStyle = 'rgba(255,255,255,0.1)';
            ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(60, 402); ctx.lineTo(W - 60, 402); ctx.stroke();

            // ── User info (bottom-left) ──────────────────────────────────────
            ctx.font = `bold 22px ${FONT}`;
            ctx.fillStyle = '#ffffff';
            ctx.textBaseline = 'alphabetic';
            ctx.fillText(userName, 60, 490);
            if (jobTitle) {
                ctx.font = `18px ${FONT}`;
                ctx.fillStyle = 'rgba(255,255,255,0.5)';
                ctx.fillText(jobTitle, 60, 522);
            }

            // ── CTA tag (bottom-left, lower) ─────────────────────────────────
            const tagY = 582;
            ctx.font = `15px ${FONT}`;
            ctx.fillStyle = 'rgba(255,255,255,0.35)';
            ctx.fillText('Track your work week at', 60, tagY);

            // ── URL (bottom-right) ───────────────────────────────────────────
            ctx.font = `bold 24px ${FONT}`;
            ctx.fillStyle = '#f59e0b';
            ctx.textAlign = 'right';
            ctx.fillText('worklog.today', W - 60, tagY);
            ctx.textAlign = 'left';

            // ── Amber bottom accent ──────────────────────────────────────────
            const grad2 = ctx.createLinearGradient(0, H - 4, W, H - 4);
            grad2.addColorStop(0, '#f59e0b');
            grad2.addColorStop(1, 'transparent');
            ctx.fillStyle = grad2;
            ctx.fillRect(0, H - 4, W, 4);

            return canvas;
        }

        function cardText() {
            const sg = $('.stat-grid');
            const totalHours = sg?.dataset.totalHours || '0';
            const taskCount  = sg?.dataset.taskCount  || '0';
            const billable   = sg?.dataset.billableHours || '0';
            const weekLabel  = sg?.dataset.weekLabel || '';
            const userName   = ($('.user-name-wrap div') || {}).textContent?.trim() || '';
            const earnings   = sg?.dataset.earnings || '';
            const earningsLine = earnings ? `\n💰 ${earnings} estimated earnings` : '';
            return `📊 My week on worklog.today (${weekLabel})\n\n⏱ ${totalHours}h logged  ✅ ${taskCount} tasks  💼 ${billable}h billable${earningsLine}\n\nTrack your work week: https://worklog.today`;
        }

        shareBtn.addEventListener('click', () => {
            buildCard();
            shareModal.classList.add('open');
        });

        $('#downloadCardBtn').addEventListener('click', () => {
            const sg = $('.stat-grid');
            const weekLabel = (sg?.dataset.weekLabel || 'week').replace(/[^a-z0-9]/gi, '-').toLowerCase();
            const link = document.createElement('a');
            link.download = `worklog-${weekLabel}.png`;
            link.href = canvas.toDataURL('image/png');
            link.click();
            toast('Card downloaded!');
        });

        $('#copyCardImgBtn').addEventListener('click', async () => {
            try {
                canvas.toBlob(async blob => {
                    try {
                        await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                        toast('Image copied to clipboard — paste into LinkedIn or Twitter!');
                    } catch {
                        toast('Copy not supported in this browser — use Download instead.');
                    }
                }, 'image/png');
            } catch { toast('Use Download PNG instead.'); }
        });

        $('#copyCardTextBtn').addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(cardText());
                toast('Caption copied — paste it with your image!');
            } catch { toast('Could not copy text.'); }
        });
    })();

    // ---------- Modals close ----------
    $$('[data-close]').forEach(b => b.addEventListener('click', () => b.closest('.modal-bg').classList.remove('open')));
    $$('.modal-bg').forEach(m => m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); }));
    document.addEventListener('keydown', e => { if (e.key === 'Escape') $$('.modal-bg.open').forEach(m => m.classList.remove('open')); });
})();
