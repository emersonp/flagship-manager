import re, html, sys
import os
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, '..', '..', 'docs', 'gdd.md'), encoding='utf-8').read().split('\n')
OUT = sys.argv[1]

def inline(t):
    parts = t.split('`')
    o = []
    for i, p in enumerate(parts):
        p = html.escape(p, quote=False)
        if i % 2:
            o.append('<code>' + p + '</code>')
        else:
            o.append(re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', p))
    return ''.join(o)

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
CALL = '''<div class="widget" id="call-calc"><p class="wlabel">Try it: call or notice</p>
<div class="wgrid">
<label>Lead caution <input type="range" id="k-caution" min="-2" max="2" step="1" value="1"><output id="k-caution-o"></output></label>
<label>Lead discipline <input type="range" id="k-disc" min="-2" max="2" step="1" value="0"><output id="k-disc-o"></output></label>
<label>Autonomy parameter <input type="range" id="k-auto" min="-1" max="1" step="1" value="0"><output id="k-auto-o"></output></label>
</div>
<p class="wout" id="k-thr"></p>
<div class="stakes" id="k-stakes"></div></div>'''

# split off open questions
start = next(i for i, l in enumerate(src) if l.startswith('## 1. '))
oq = next(i for i, l in enumerate(src) if re.match(r'## \d+\. Open questions', l))
QN = re.match(r'## (\d+)', src[oq]).group(1)
body, oqs = src[start:oq], src[oq + 1:]

out, nav = [], []
stack = []  # list indents/types
para = []
def flush_para():
    global para
    if para:
        out.append('<p>' + inline(' '.join(para)) + '</p>'); para = []
def close_lists(to=-1):
    while stack and stack[-1][0] > to:
        out.append('</li></%s>' % stack.pop()[1])
i = 0
open_section = False
while i < len(body):
    l = body[i]
    if l.startswith('```'):
        flush_para(); close_lists()
        j = i + 1; code = []
        while not body[j].startswith('```'):
            code.append(body[j]); j += 1
        out.append('<div class="scroll"><pre><code>' + html.escape('\n'.join(code), quote=False) + '</code></pre></div>')
        i = j + 1; continue
    m = re.match(r'^(#{2,3}) (.+)$', l)
    if m:
        flush_para(); close_lists()
        title = m.group(2)
        if m.group(1) == '##':
            num, name = title.split('. ', 1)
            if open_section: out.append('</section>')
            out.append('<section id="s%s"><h2><span class="num">%s</span>%s</h2>' % (num, num.zfill(2), inline(name)))
            nav.append((num, name)); open_section = True
        else:
            if title.startswith('4.3'): out.append(CHECK)
            if title.startswith('4.5'): out.append(CALL)
            out.append('<h3>' + inline(title) + '</h3>')
        i += 1; continue
    if l.startswith('|'):
        flush_para(); close_lists()
        rows = []
        while i < len(body) and body[i].startswith('|'):
            rows.append([c.strip() for c in body[i].strip().strip('|').split('|')]); i += 1
        t = ['<div class="scroll"><table><thead><tr>' + ''.join('<th>%s</th>' % inline(c) for c in rows[0]) + '</tr></thead><tbody>']
        for r in rows[2:]:
            status = r[-1].strip() if rows[0][-1].strip() == 'Status' else ''
            key = 'done' if status.startswith('Done') else 'now' if status.startswith('In progress') else 'next' if status == 'Next' else 'later' if status else ''
            cells = ['<td>%s</td>' % inline(c) for c in r]
            if key:
                cells[-1] = '<td><span class="st st-%s">%s</span></td>' % (key, inline(r[-1]))
            t.append('<tr%s>' % (' class="row-%s"' % key if key else '') + ''.join(cells) + '</tr>')
        t.append('</tbody></table></div>'); out.append(''.join(t)); continue
    m = re.match(r'^(\s*)(\d+\.|-) (.+)$', l)
    if m:
        flush_para()
        ind = len(m.group(1)); typ = 'ul' if m.group(2) == '-' else 'ol'
        close_lists(ind)
        if stack and stack[-1][0] == ind:
            out.append('</li>')
        else:
            out.append('<%s>' % typ); stack.append((ind, typ))
        out.append('<li>' + inline(m.group(3)))
        i += 1; continue
    if not l.strip():
        flush_para(); i += 1; continue
    if stack and l.startswith(' '):
        out.append(' ' + inline(l.strip())); i += 1; continue
    close_lists(); para.append(l.strip()); i += 1
flush_para(); close_lists()
if open_section: out.append('</section>')

# open questions
qs = []; cur = None
for l in oqs:
    m = re.match(r'^(\d+)\. \*\*(.+?)\*\*$', l)
    if m:
        cur = {'n': m.group(1), 't': m.group(2), 'lines': []}; qs.append(cur)
    elif cur and l.strip():
        cur['lines'].append(l.strip())
q = ['<section id="s%s"><h2><span class="num">%s</span>Open questions</h2>' % (QN, QN),
     '<p>Pick an answer for each. Your choices stay in this browser. Copy them and paste them back into the session to have them folded into the document.</p>',
     '<div class="qtools"><button type="button" id="q-rec">Accept all recommendations</button><button type="button" id="q-copy">Copy my answers</button><span id="q-status" role="status"></span></div>',
     '<textarea id="q-out" rows="3" readonly hidden aria-label="Your answers"></textarea>']
for item in qs:
    n = item['n']; opts = None; rest = []
    for ln in item['lines']:
        if ln.startswith('(a)'):
            opts = re.findall(r'\(([a-c])\)\s*(.+?)(?=\s*\([a-c]\)|$)', ln)
        else:
            rest.append(ln)
    rec = None
    for ln in rest:
        m = re.match(r'^Recommend \(([a-c])\)', ln)
        if m: rec = m.group(1)
    q.append('<fieldset class="q" data-q="%s" data-rec="%s"><legend><span class="qn">%s</span>%s</legend>' % (n, rec or '', n, inline(item['t'])))
    if opts:
        q.append('<div class="opts">')
        for k, txt in opts:
            q.append('<label class="opt%s"><input type="radio" name="q%s" id="q%s%s" value="%s"><span><b>%s</b> %s%s</span></label>' % (
                ' rec' if k == rec else '', n, n, k, k, k, inline(txt.rstrip('. ') ), ' <em>recommended</em>' if k == rec else ''))
        q.append('</div>')
    for ln in rest:
        q.append('<p class="why">' + inline(ln) + '</p>')
    q.append('<input type="text" class="note" id="q%snote" placeholder="%s" aria-label="Note for question %s">' % (n, 'Your answer' if not opts else 'Note (optional)', n))
    q.append('</fieldset>')
if not qs: q = ['<section id="s%s"><h2><span class="num">%s</span>Open questions</h2><p>None at present. New ones are added here as they arise.</p><div hidden><button id="q-rec"></button><button id="q-copy"></button><span id="q-status"></span><textarea id="q-out"></textarea></div>' % (QN, QN)]
q.append('</section>')
nav.append((QN, 'Open questions'))

navhtml = ''.join('<a href="#s%s"><span>%s</span>%s</a>' % (n, n.zfill(2), html.escape(t)) for n, t in nav)
tpl = open(sys.argv[2], encoding='utf-8').read()
open(OUT, 'w', encoding='utf-8').write(tpl.replace('{{NAV}}', navhtml).replace('{{BODY}}', '\n'.join(out) + '\n' + '\n'.join(q)))
print(len(qs), 'questions;', len(nav), 'sections')
