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

    // ---------- Tabs ----------
    function activateTab(name) {
        $$('.app-tab').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
        $$('.bottom-nav-item').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
        $$('.tab-pane').forEach(p => p.classList.toggle('active', p.id === 'pane-' + name));
        localStorage.setItem('wt_tab', name);
        if (name === 'reports') loadReports();
        // FAB: show on notes, hide on others
        const fab = $('#mobileFab');
        if (fab) fab.style.display = name === 'notes' ? '' : 'none';
    }
    $$('.app-tab').forEach(b => b.addEventListener('click', () => activateTab(b.dataset.tab)));
    $$('.bottom-nav-item').forEach(b => b.addEventListener('click', () => activateTab(b.dataset.tab)));
    const urlTab = new URLSearchParams(location.search).get('tab');
    const savedTab = urlTab || localStorage.getItem('wt_tab');
    if (savedTab && $('#pane-' + savedTab)) activateTab(savedTab);

    // ---------- Mobile FAB ----------
    const mobileFab = $('#mobileFab');
    if (mobileFab) {
        mobileFab.addEventListener('click', () => {
            // Expand the composer and focus it
            const composer = $('#composer');
            const body = $('#cBody');
            if (composer && body) {
                composer.classList.add('expanded');
                body.focus();
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
        });
    }

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
    function expand() { composer.classList.add('expanded'); }
    function collapse() {
        if (!cTitle.value && !cBody.value && !cLabels.value) { composer.classList.remove('expanded'); }
    }
    cBody.addEventListener('focus', expand);
    cBody.addEventListener('input', () => { cBody.style.height = 'auto'; cBody.style.height = cBody.scrollHeight + 'px'; });
    document.addEventListener('click', e => { if (!composer.contains(e.target)) collapse(); });
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

    $('#saveNoteBtn').addEventListener('click', async () => {
        if (!cBody.value && !cTitle.value) return toast('Empty note');
        try {
            const note = await api('POST', '/api/notes', { title: cTitle.value, content: cBody.value, colorHex: curColor, labels: cLabels.value });
            addNoteToDom(note, true);
            cTitle.value = cBody.value = cLabels.value = '';
            cBody.style.height = 'auto'; curColor = '#ffffff'; composer.style.background = '';
            $$('#swatches .sw').forEach(s => s.classList.toggle('active', s.dataset.color === '#ffffff'));
            composer.classList.remove('expanded');
            $('#notesEmpty').style.display = 'none';
            toast('Note added');
        } catch (e) { toast(e.message); }
    });

    const COLORS = ["#ffffff","#fff8c5","#d3f9d8","#dbeafe","#fbe4ff","#ffe8cc","#ffd6d6"];

    function noteCardHtml(n) {
        const labels = (n.labels || '').split(',').map(s => s.trim()).filter(Boolean);
        const swatches = COLORS.map(c =>
            `<span class="nsw${c === n.colorHex ? ' active' : ''}" style="background:${c}" data-color="${c}"></span>`
        ).join('');
        return `
          <button class="mini-btn pin ${n.isPinned ? 'on' : ''}" type="button" data-act="pin" title="Pin note"><i class="bi ${n.isPinned ? 'bi-pin-angle-fill' : 'bi-pin-angle'}"></i></button>
          <div class="ntitle-edit" contenteditable="true" data-placeholder="Title" spellcheck="true">${esc(n.title || '')}</div>
          <div class="ntext-edit" contenteditable="true" data-placeholder="Add a note…" spellcheck="true">${esc(n.content || '')}</div>
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

    // ── Inline note editing (Google Keep-style) ──────────────────────────────
    let activeCard = null;
    let autoSaveTimer = null;

    function openNoteInline(card) {
        if (activeCard === card) return;
        if (activeCard) closeNoteInline(activeCard);
        activeCard = card;
        card.classList.add('note-open');
        card.querySelector('.ntitle-edit').focus();
    }

    async function closeNoteInline(card) {
        if (!card || !card.classList.contains('note-open')) return;
        card.classList.remove('note-open');
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

    // Click on card body → open inline edit
    document.addEventListener('click', e => {
        const card = e.target.closest('.note');
        if (!card) { if (activeCard) closeNoteInline(activeCard); return; }

        // Don't intercept action buttons
        const btn = e.target.closest('[data-act]');
        if (btn) return;

        openNoteInline(card);
    });

    // Auto-save on typing inside an open card
    document.addEventListener('input', e => {
        const card = e.target.closest('.note.note-open');
        if (card) scheduleAutoSave(card);
    });

    // Color swatch click inside card
    document.addEventListener('click', async e => {
        const swatch = e.target.closest('.note .nsw');
        if (!swatch) return;
        const card = swatch.closest('.note');
        const color = swatch.dataset.color;
        card.style.background = color;
        card.dataset.color = color;
        card.querySelectorAll('.nsw').forEach(s => s.classList.toggle('active', s.dataset.color === color));
        scheduleAutoSave(card);
    });

    // Note action buttons (event delegation)
    document.addEventListener('click', async e => {
        const btn = e.target.closest('.note [data-act]'); if (!btn) return;
        const card = btn.closest('.note'); const id = card.dataset.id; const act = btn.dataset.act;
        try {
            if (act === 'close') {
                await closeNoteInline(card);
            } else if (act === 'pin') {
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

    // ---------- Search ----------
    $('#noteSearch').addEventListener('input', e => {
        const q = e.target.value.toLowerCase();
        $$('.note').forEach(c => {
            const txt = (c.textContent + ' ' + (c.dataset.labels || '')).toLowerCase();
            c.style.display = txt.includes(q) ? '' : 'none';
        });
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
    $('#taskBody').addEventListener('click', async e => {
        const row = e.target.closest('tr'); if (!row) return;
        if (e.target.closest('.editTask')) openTaskModal(row);
        if (e.target.closest('.delTask')) {
            if (!confirm('Delete this task?')) return;
            try { await api('DELETE', `/api/work/${row.dataset.id}`); row.remove(); taskEmptyCheck(); toast('Deleted'); }
            catch (err) { toast(err.message); }
        }
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
                localStorage.setItem(eodKey, '1');
                modal.classList.remove('open');
                toast('Logged! Great work today 🎉');
                setTimeout(() => location.reload(), 800);
            } catch (e) { toast('⚠ ' + e.message); }
        });

        $$('[data-close]', modal).forEach(b => b.addEventListener('click', () => {
            localStorage.setItem(eodKey, '1'); // skip today if dismissed
            modal.classList.remove('open');
        }));
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
                company: $('#sCompany').value.trim()
            });
            settingsModal.classList.remove('open');
            toast('Settings saved!');
            setTimeout(() => location.reload(), 600);
        } catch (e) { toast('⚠ ' + e.message); }
    });

    // ---------- Modals close ----------
    $$('[data-close]').forEach(b => b.addEventListener('click', () => b.closest('.modal-bg').classList.remove('open')));
    $$('.modal-bg').forEach(m => m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); }));
    document.addEventListener('keydown', e => { if (e.key === 'Escape') $$('.modal-bg.open').forEach(m => m.classList.remove('open')); });
})();
