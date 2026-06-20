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
    function toast(msg) {
        const t = $('#toast'); t.textContent = msg; t.classList.add('show');
        clearTimeout(toastT); toastT = setTimeout(() => t.classList.remove('show'), 2200);
    }

    // ---------- Tabs ----------
    function activateTab(name) {
        $$('.app-tab').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
        $$('.tab-pane').forEach(p => p.classList.toggle('active', p.id === 'pane-' + name));
        localStorage.setItem('wt_tab', name);
        if (name === 'reports') loadReports();
    }
    $$('.app-tab').forEach(b => b.addEventListener('click', () => activateTab(b.dataset.tab)));
    const urlTab = new URLSearchParams(location.search).get('tab');
    const savedTab = urlTab || localStorage.getItem('wt_tab');
    if (savedTab && $('#pane-' + savedTab)) activateTab(savedTab);

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
    window.addEventListener('appinstalled', () => { if (installBtn) installBtn.style.display = 'none'; toast('Installed! Find worklog on your desktop.'); });

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
            cLabels.value = r.labels; toast('AI tags via ' + r.source);
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

    function noteCardHtml(n) {
        const labels = (n.labels || '').split(',').map(s => s.trim()).filter(Boolean);
        return `
          <button class="mini-btn pin ${n.isPinned ? 'on' : ''}" type="button" data-act="pin" title="Pin"><i class="bi ${n.isPinned ? 'bi-pin-angle-fill' : 'bi-pin-angle'}"></i></button>
          ${n.title ? `<h4 class="ntitle">${esc(n.title)}</h4>` : ''}
          <div class="ntext">${esc(n.content)}</div>
          ${labels.length ? `<div class="nlabels">${labels.map(l => `<span class="nlabel">${esc(l)}</span>`).join('')}</div>` : ''}
          <div class="nactions">
            <button class="mini-btn" type="button" data-act="popout" title="Pop out as desktop sticky"><i class="bi bi-window-stack"></i></button>
            <button class="mini-btn" type="button" data-act="edit" title="Edit"><i class="bi bi-pencil"></i></button>
            <button class="mini-btn" type="button" data-act="archive" title="Archive"><i class="bi bi-archive"></i></button>
            <button class="mini-btn" type="button" data-act="delete" title="Delete"><i class="bi bi-trash"></i></button>
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

    // Note actions (event delegation)
    document.addEventListener('click', async e => {
        const btn = e.target.closest('.note [data-act]'); if (!btn) return;
        const card = btn.closest('.note'); const id = card.dataset.id; const act = btn.dataset.act;
        try {
            if (act === 'pin') {
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
            } else if (act === 'edit') {
                openNoteModal(card);
            }
        } catch (err) { toast(err.message); }
    });

    // Note edit modal
    const noteModal = $('#noteModal');
    function openNoteModal(card) {
        $('#nId').value = card.dataset.id;
        $('#nTitle').value = card.querySelector('.ntitle')?.textContent || '';
        $('#nBody').value = card.querySelector('.ntext')?.textContent || '';
        $('#nLabels').value = card.dataset.labels || '';
        noteModal.classList.add('open');
    }
    $('#saveNoteEditBtn').addEventListener('click', async () => {
        const id = $('#nId').value;
        const card = $(`.note[data-id="${id}"]`);
        try {
            const n = await api('PUT', `/api/notes/${id}`, { title: $('#nTitle').value, content: $('#nBody').value, colorHex: card.dataset.color, labels: $('#nLabels').value });
            const fresh = makeNoteEl({ ...n });
            card.replaceWith(fresh);
            noteModal.classList.remove('open'); toast('Saved');
        } catch (e) { toast(e.message); }
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
            <td>${esc(w.project) || '—'}</td>
            <td><span class="pill ${catCls(w.category)}">${w.categoryName}</span></td>
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

    // ---------- AI summary ----------
    $('#genSummaryBtn').addEventListener('click', async () => {
        const btn = $('#genSummaryBtn'); const out = $('#aiOut'); const src = $('#aiSource');
        btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Thinking…';
        out.textContent = ''; src.textContent = '';
        try {
            const r = await api('GET', `/api/work/summary?from=${RANGE.from}&to=${RANGE.to}`);
            out.textContent = r.text;
            src.innerHTML = `<i class="bi bi-cpu"></i> Generated by <strong>${r.source === 'ollama' ? 'Ollama (local model)' : 'local engine'}</strong> · ${r.count} entries · ${r.hours} h`;
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

    // ---------- Modals close ----------
    $$('[data-close]').forEach(b => b.addEventListener('click', () => b.closest('.modal-bg').classList.remove('open')));
    $$('.modal-bg').forEach(m => m.addEventListener('click', e => { if (e.target === m) m.classList.remove('open'); }));
    document.addEventListener('keydown', e => { if (e.key === 'Escape') $$('.modal-bg.open').forEach(m => m.classList.remove('open')); });
})();
