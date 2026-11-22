"""Builds the GDD's pages: self-contained HTML files from docs/gdd.md (the index) and docs/gdd/*.md (the chapters).

    python tools/gdd-page/build.py OUT.html tools/gdd-page/template.html             the whole GDD
    python tools/gdd-page/build.py OUT.html tools/gdd-page/template.html --progress  Ready Room Progress

The progress page is the overview's "Where the build stands", chapter 11 (the roadmap and section 11.1), and
chapter 16 (the open questions); a reference to any other section links to the whole GDD's page. The two pages'
addresses are in pages.json beside this script.

Standard library only. The page shows one chapter at a time, routed on the hash (#s4 for chapter 4,
#s4-2 for section 4.2), with the parts and chapters in a sidebar, a search over every section, and an
overview built from the index and the progress table in section 11.1.
"""
import html
import json
import os
import re
import sys
from datetime import date, datetime, time, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.normpath(os.path.join(HERE, '..', '..', 'docs'))
OUT, TEMPLATE = sys.argv[1], sys.argv[2]
PROGRESS = '--progress' in sys.argv[3:]
NAME = 'Flagship: Ready Room'
PAGES = json.load(open(os.path.join(HERE, 'pages.json'), encoding='utf-8'))
LOCAL_CHAPTERS = {11, 16} if PROGRESS else None  # None: every chapter is on this page


def pacific_now():
    """Now in US Pacific time, by the US daylight-saving rule (no tz database needed)."""
    u = datetime.now(timezone.utc)

    def sunday(month, n):
        d = date(u.year, month, 1)
        return d + timedelta((6 - d.weekday()) % 7 + 7 * (n - 1))

    starts = datetime.combine(sunday(3, 2), time(10), timezone.utc)  # 2:00 PST
    ends = datetime.combine(sunday(11, 1), time(9), timezone.utc)    # 2:00 PDT
    dst = starts <= u < ends
    return u + timedelta(hours=-7 if dst else -8), 'PDT' if dst else 'PST'


# ---- The index: status, parts, chapters ----------------------------------------------------------

index = open(os.path.join(DOCS, 'gdd.md'), encoding='utf-8').read().split('\n')
status = next(l for l in index if l.startswith('Status:'))
version = re.search(r'v(\d+\.\d+)', status).group(0)
preface = []  # the paragraphs between the status line and the first part
parts = []    # (name, intro, [(num, title, file, desc)])
cur = None
for l in index:
    m = re.match(r'^## (.+)$', l)
    if m:
        cur = None if m.group(1).startswith('How this document') else {'name': m.group(1), 'intro': '', 'chapters': []}
        if cur:
            parts.append(cur)
        continue
    if cur is None:
        if not parts and l.strip() and not l.startswith(('#', 'Status:', '-')):
            preface.append(l.strip())
        continue
    m = re.match(r'^\| (\d+) \| \[(.+?)\]\((.+?)\) \| (.+?) \|$', l)
    if m:
        cur['chapters'].append((int(m.group(1)), m.group(2), m.group(3), m.group(4)))
    elif l.strip() and not l.startswith('|') and not cur['intro']:
        cur['intro'] = l.strip()

all_order = [c for p in parts for c in p['chapters']]
order = [c for c in all_order if LOCAL_CHAPTERS is None or c[0] in LOCAL_CHAPTERS]
part_of = {c[0]: p['name'] for p in parts for c in p['chapters']}
file_to_num = {os.path.basename(c[2]): c[0] for c in all_order}
chapters = {}
for num, title, path, desc in all_order:
    chapters[num] = open(os.path.join(DOCS, path), encoding='utf-8').read().split('\n')

# Every section id, so cross-references link only to sections that exist.
known = set(str(n) for n in chapters)
for lines in chapters.values():
    for l in lines:
        m = re.match(r'^## (\d+\.\d+) ', l)
        if m:
            known.add(m.group(1))


def sid(ref):
    return 's' + ref.replace('.', '-')


def href(anchor):
    """A link to a chapter or section: on this page when it is here, otherwise on the other page."""
    num = int(re.match(r'^s(\d+)', anchor).group(1))
    if LOCAL_CHAPTERS is None or num in LOCAL_CHAPTERS:
        return 'href="#%s"' % anchor
    return 'href="%s#%s" target="_blank" rel="noopener"' % (PAGES['gdd'], anchor)


# ---- Inline markdown -----------------------------------------------------------------------------

NUM = r'\d+(?:\.\d+)?'
LIST = NUM + r'(?:(?:,? and |, or |, | to )' + NUM + r')*'


def link_refs(t):
    def each(s):
        return re.sub(NUM, lambda m: '<a class="xref" %s>%s</a>' % (href(sid(m.group(0))), m.group(0)) if m.group(0) in known else m.group(0), s)
    t = re.sub(r'\b([Ss]ections? |[Ss]ee )(' + LIST + r')', lambda m: m.group(1) + each(m.group(2)), t)
    t = re.sub(r'\((\d+\.\d+)\)', lambda m: '(' + each(m.group(1)) + ')', t)
    t = re.sub(r'\b(risk )(\d+)\b', lambda m: '<a class="xref" %s>%s%s</a>' % (href('s14'), m.group(1), m.group(2)), t)
    return t


def md_link(m):
    target = file_to_num.get(os.path.basename(m.group(2)))
    return '<a %s>%s</a>' % (href('s%d' % target), m.group(1)) if target else m.group(1)


def inline(t):
    o = []
    for i, p in enumerate(t.split('`')):
        p = html.escape(p, quote=False)
        if i % 2:
            o.append('<code>' + p + '</code>')
        else:
            p = re.sub(r'\[(.+?)\]\((.+?)\)', md_link, p)
            p = link_refs(p)
            o.append(re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', p))
    return ''.join(o)


# ---- Widgets placed in chapter 4 -----------------------------------------------------------------

CHECK = '''<div class="widget" id="check-calc"><p class="wlabel">Try it: the check</p>
<div class="wgrid">
<label>Actor skill <input type="number" id="c-skill" min="0" max="10" value="4"></label>
<label>Second-best skill <input type="number" id="c-assist" min="0" max="10" value="6"></label>
<label>Gear and traits <input type="number" id="c-gear" min="-5" max="8" value="2"></label>
<label>Momentum <input type="number" id="c-mom" min="0" max="5" value="0"></label>
<label>Strain <input type="number" id="c-strain" min="0" max="5" value="1"></label>
<label>Difficulty <input type="number" id="c-diff" min="1" max="15" value="6"></label>
</div>
<p class="wout" id="c-score"></p>
<div class="bar" id="c-bar" role="img" aria-label="Outcome probabilities"></div>
<ul class="legend" id="c-legend"></ul></div>'''
CALL = '''<div class="widget" id="call-calc"><p class="wlabel">Try it: call or initiative</p>
<div class="wgrid">
<label>Lead caution <input type="range" id="k-caution" min="-2" max="2" step="1" value="1"><output id="k-caution-o"></output></label>
<label>Lead discipline <input type="range" id="k-disc" min="-2" max="2" step="1" value="0"><output id="k-disc-o"></output></label>
<label>Autonomy parameter <input type="range" id="k-auto" min="-1" max="1" step="1" value="0"><output id="k-auto-o"></output></label>
</div>
<p class="wout" id="k-thr"></p>
<div class="stakes" id="k-stakes"></div></div>'''
WIDGET_BEFORE = {'4.3': CHECK, '4.5': CALL}


# ---- Block markdown ------------------------------------------------------------------------------

def status_key(s):
    return 'done' if s.startswith('Done') else 'now' if s.startswith('In progress') else 'next' if s == 'Next' else 'later' if s else ''


def render(lines):
    """Render one chapter's body (its title line and the 'A chapter of' line already removed)."""
    out, stack, para = [], [], []
    open_sec = False

    def flush():
        if para:
            out.append('<p>' + inline(' '.join(para)) + '</p>')
            para.clear()

    def close_lists(to=-1):
        while stack and stack[-1][0] > to:
            out.append('</li></%s>' % stack.pop()[1])

    out.append('<div class="intro">')
    i = 0
    while i < len(lines):
        l = lines[i]
        if l.startswith('```'):
            flush(); close_lists()
            j = i + 1
            while not lines[j].startswith('```'):
                j += 1
            out.append('<div class="scroll"><pre><code>' + html.escape('\n'.join(lines[i + 1:j]), quote=False) + '</code></pre></div>')
            i = j + 1
            continue
        m = re.match(r'^(#{2,3}) (.+)$', l)
        if m:
            flush(); close_lists()
            title = m.group(2)
            if m.group(1) == '##':
                ref = title.split(' ', 1)[0]
                if ref in WIDGET_BEFORE:
                    out.append(WIDGET_BEFORE[ref])
                out.append('</section>' if open_sec else '</div>')
                num, name = title.split(' ', 1)
                out.append('<section class="sec" id="%s"><h2><span class="num">%s</span>%s</h2>' % (sid(num), num, inline(name)))
                open_sec = True
            else:
                out.append('<h3>' + inline(title) + '</h3>')
            i += 1
            continue
        if l.startswith('|'):
            flush(); close_lists()
            rows = []
            while i < len(lines) and lines[i].startswith('|'):
                rows.append([c.strip() for c in lines[i].strip().strip('|').split('|')])
                i += 1
            is_status = rows[0][-1] == 'Status'
            t = ['<div class="scroll"><table><thead><tr>' + ''.join('<th>%s</th>' % inline(c) for c in rows[0]) + '</tr></thead><tbody>']
            for r in rows[2:]:
                key = status_key(r[-1]) if is_status else ''
                cells = ['<td>%s</td>' % inline(c) for c in r]
                if key:
                    cells[-1] = '<td><span class="st st-%s">%s</span></td>' % (key, inline(r[-1]))
                t.append('<tr%s>' % (' class="row-%s"' % key if key else '') + ''.join(cells) + '</tr>')
            t.append('</tbody></table></div>')
            out.append(''.join(t))
            continue
        m = re.match(r'^(\s*)(\d+\.|-) (.+)$', l)
        if m:
            flush()
            ind, typ = len(m.group(1)), 'ul' if m.group(2) == '-' else 'ol'
            close_lists(ind)
            if stack and stack[-1][0] == ind:
                out.append('</li>')
            else:
                out.append('<%s>' % typ)
                stack.append((ind, typ))
            out.append('<li>' + inline(m.group(3)))
            i += 1
            continue
        if not l.strip():
            flush(); i += 1
            continue
        if stack and l.startswith(' '):
            out.append(' ' + inline(l.strip())); i += 1
            continue
        close_lists(); para.append(l.strip()); i += 1
    flush(); close_lists()
    out.append('</section>' if open_sec else '</div>')
    return '\n'.join(out)


def render_questions(lines):
    qs, cur = [], None
    for l in lines:
        m = re.match(r'^(\d+)\. \*\*(.+?)\*\*$', l)
        if m:
            cur = {'n': m.group(1), 't': m.group(2), 'lines': []}
            qs.append(cur)
        elif cur and l.strip():
            cur['lines'].append(l.strip())
    lead = [l.strip() for l in lines[:next((k for k, l in enumerate(lines) if re.match(r'^\d+\. ', l)), len(lines))] if l.strip()]
    q = ['<div class="intro">'] + ['<p>' + inline(p) + '</p>' for p in lead]
    if not qs:
        q.append('<p>None at present. New ones are added here as they arise.</p></div>')
        return '\n'.join(q)
    q += ['<p>Pick an answer for each. Your choices stay in this browser. Copy them and paste them back into the session to have them folded into the document.</p>',
          '<div class="qtools"><button type="button" id="q-rec">Accept all recommendations</button><button type="button" id="q-copy">Copy my answers</button><span id="q-status" role="status"></span></div>',
          '<textarea id="q-out" rows="3" readonly hidden aria-label="Your answers"></textarea></div>']
    for item in qs:
        n, opts, rest, rec = item['n'], None, [], None
        for ln in item['lines']:
            if ln.startswith('(a)'):
                opts = re.findall(r'\(([a-d])\)\s*(.+?)(?=\s*\([a-d]\)|$)', ln)
            else:
                rest.append(ln)
                m = re.match(r'^Recommend \(([a-d])\)', ln)
                if m:
                    rec = m.group(1)
        q.append('<fieldset class="q" id="s16-q%s" data-q="%s" data-rec="%s"><legend><span class="qn">%s</span>%s</legend>' % (n, n, rec or '', n, inline(item['t'])))
        if opts:
            q.append('<div class="opts">')
            for k, txt in opts:
                q.append('<label class="opt%s"><input type="radio" name="q%s" id="q%s%s" value="%s"><span><b>%s</b> %s%s</span></label>' % (
                    ' rec' if k == rec else '', n, n, k, k, k, inline(txt.rstrip('. ')), ' <em>recommended</em>' if k == rec else ''))
            q.append('</div>')
        q += ['<p class="why">' + inline(ln) + '</p>' for ln in rest]
        q.append('<input type="text" class="note" id="q%snote" placeholder="%s" aria-label="Note for question %s">' % (n, 'Your answer' if not opts else 'Note (optional)', n))
        q.append('</fieldset>')
    return '\n'.join(q)


# ---- Chapters ------------------------------------------------------------------------------------

articles, navparts = [], []
for k, (num, title, path, desc) in enumerate(order):
    lines = chapters[num]
    body_lines = [l for l in lines[1:] if not l.startswith('A chapter of the [')]
    body = render_questions(body_lines) if num == 16 else render(body_lines)
    prev_c = order[k - 1] if k else None
    next_c = order[k + 1] if k + 1 < len(order) else None
    pager = '<nav class="pager" aria-label="Chapters">%s%s</nav>' % (
        '<a class="prev" href="#s%d"><small>Previous</small>%d. %s</a>' % (prev_c[0], prev_c[0], html.escape(prev_c[1])) if prev_c else '<span></span>',
        '<a class="next" href="#s%d"><small>Next</small>%d. %s</a>' % (next_c[0], next_c[0], html.escape(next_c[1])) if next_c else '<span></span>')
    articles.append(
        '<article class="chapter" id="s%d" data-title="%s">'
        '<header class="chead"><p class="eyebrow">%s</p><h1><span class="num">%d</span>%s</h1><p class="desc">%s</p></header>'
        '%s<footer class="cfoot"><p class="src">Source: <code>docs/%s</code></p>%s</footer></article>' % (
            num, html.escape(title), html.escape(part_of[num]), num, html.escape(title), inline(desc), body, path, pager))


def nav_sections(num):
    secs = [re.match(r'^## (\d+\.\d+) (.+)$', l) for l in chapters[num]]
    secs = [m for m in secs if m]
    if not secs:
        return ''
    return '<ul class="secs">' + ''.join('<li><a href="#%s"><span>%s</span>%s</a></li>' % (sid(m.group(1)), m.group(1), inline(m.group(2))) for m in secs) + '</ul>'


nav = ['<a class="home" href="#home" data-ch="home">Overview</a>']
for p in parts:
    if not any(c in order for c in p['chapters']):
        continue
    nav.append('<p class="part">%s</p><ul class="chs">' % html.escape(p['name']))
    for num, title, path, desc in [c for c in p['chapters'] if c in order]:
        nav.append('<li data-ch="s%d"><a href="#s%d"><span>%d</span>%s</a>%s</li>' % (num, num, num, html.escape(title), nav_sections(num)))
    nav.append('</ul>')
if PROGRESS:
    nav.append('<p class="part">Elsewhere</p><ul class="chs"><li><a href="%s" target="_blank" rel="noopener"><span>&#8599;</span>The whole GDD</a></li></ul>' % PAGES['gdd'])


# ---- The overview --------------------------------------------------------------------------------

rows = []
for l in chapters[11]:
    if l.startswith('| ') and not l.startswith(('| Phase |', '|---')):
        cells = [c.strip() for c in l.strip().strip('|').split('|')]
        if len(cells) == 4:
            rows.append(cells)
phase1 = [r for r in rows if r[0].startswith('1.')]
done1 = [r for r in phase1 if r[3].startswith('Done')]
current = next((r for r in rows if r[3].startswith('In progress')), None) or next((r for r in rows if r[3] == 'Next'), None)
last_done = done1[-1] if done1 else None
phases = [r for r in rows if re.match(r'^\d+\. ', r[0])]
phase_count = len({r[0] for r in phases})
pct = round(100 * len(done1) / len(phase1)) if phase1 else 0

ticks = ''.join('<i class="t-%s" title="%s"></i>' % (status_key(r[3]) or 'later', html.escape(r[1] + ': ' + r[3])) for r in phase1)
progress = ['<section class="where" aria-labelledby="where-h"><h2 id="where-h">Where the build stands</h2>',
            '<p class="phase"><b>Phase 1 of 0 to %d</b> · %s · %d of %d slices done</p>' % (phase_count - 1, html.escape(phase1[0][0].split('. ', 1)[1]), len(done1), len(phase1)),
            '<div class="ticks" role="img" aria-label="%d of %d Phase 1 slices done">%s</div>' % (len(done1), len(phase1), ticks)]
if current:
    progress.append('<div class="slice now"><p class="label">%s</p><p class="name">%s</p><p>%s</p></div>' % (
        'In progress' if current[3].startswith('In progress') else 'Next', inline(current[1]), inline(current[2])))
if last_done:
    progress.append('<div class="slice"><p class="label">Last done · %s</p><p class="name">%s</p></div>' % (html.escape(last_done[3][5:]), inline(last_done[1])))
progress.append('<p class="more"><a %s>Every slice, in section 11.1</a>%s</p></section>' % (
    href('s11-1'), '' if PROGRESS else ' · <a href="%s" target="_blank" rel="noopener">Ready Room Progress</a>, republished as the build moves' % PAGES['progress']))

cards = []
for p in parts if not PROGRESS else []:
    items = ''.join('<li><a %s><span class="n">%d</span><span class="t">%s</span><span class="d">%s</span></a></li>' % (
        href('s%d' % n), n, html.escape(t), inline(d)) for n, t, f, d in p['chapters'])
    cards.append('<section class="partcard"><h2>%s</h2><p>%s</p><ul>%s</ul></section>' % (html.escape(p['name']), inline(p['intro']), items))

pt, zone = pacific_now()
updated = '%s, %s %s' % (pt.strftime('%a %d %b %Y').replace(' 0', ' '), pt.strftime('%I:%M %p').lstrip('0'), zone)
KIND = 'Progress' if PROGRESS else 'Game design document'
home = ('<article class="chapter" id="home"><header class="hero"><p class="eyebrow">' + KIND + ' · %s · Updated %s</p>'
        '<h1>' + NAME + '</h1>'
        '<p class="lede">Hold the chair of a ship. Choose who goes, who leads, and who stays; then live with what follows aboard. '
        'Skill, experience, loyalty, and the crew\'s own qualities all pull on every choice, and the consequences stay. One player\'s whole game is a legacy.</p>'
        '<p class="status">%s</p>%s</header>%s<div class="parts">%s</div></article>') % (
    version, updated, inline(status[len('Status: '):]),
    ('<p class="status">The roadmap and the open questions, from the GDD. Every other section is in <a href="%s" target="_blank" rel="noopener">the whole GDD</a>, which is republished when its chapters change.</p>' % PAGES['gdd'])
    if PROGRESS else ''.join('<p class="status">' + inline(p) + '</p>' for p in preface),
    '\n'.join(progress), '\n'.join(cards))

tpl = open(TEMPLATE, encoding='utf-8').read()
page = (tpl.replace('{{NAV}}', '\n'.join(nav))
           .replace('{{BODY}}', home + '\n' + '\n'.join(articles))
           .replace('{{UPDATED}}', html.escape(updated))
           .replace('{{VERSION}}', version)
           .replace('{{TITLE}}', 'Ready Room Progress' if PROGRESS else NAME + ' GDD')
           .replace('{{BRAND}}', NAME)
           .replace('{{KIND}}', 'Progress' if PROGRESS else 'GDD')
           .replace('{{SEARCH}}', 'Search the roadmap' if PROGRESS else 'Search the GDD'))
open(OUT, 'w', encoding='utf-8').write(page)
print('%s: %d chapters; %s; updated %s' % ('Progress' if PROGRESS else 'GDD', len(order), version, updated))
