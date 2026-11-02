# -*- coding: utf-8 -*-
"""CER 质量闭环(SOP §4):faster-whisper 转写 vs 原文,输出每句 CER
用法: python cer_check.py [wav_dir]  (台词从 nar_lines.json 读)"""
import json, os, re, sys
from faster_whisper import WhisperModel

HERE = os.path.dirname(os.path.abspath(__file__))
NAR_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', 'remotion', 'public', 'audio', 'nar')

def norm(t):
    return re.sub(r'[\s，。；：、！？——\-.,;:!?()（）"\'"“”]', '', t).lower()

def cer(ref, hyp):
    r, h = norm(ref), norm(hyp)
    # DP 编辑距离
    prev = list(range(len(h) + 1))
    for i, rc in enumerate(r, 1):
        cur = [i]
        for j, hc in enumerate(h, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (rc != hc)))
        prev = cur
    return prev[len(h)] / max(1, len(r))

LINES = json.load(open(os.path.join(HERE, 'nar_lines.json'), encoding='utf-8'))
# ASR 噪声白名单(命名误识不算语音问题, SOP §4)
NOISE = [('洛天依', '骆天衣'), ('agent', 'agent'), ('hud', 'hud')]

model = WhisperModel('small', device='cpu', compute_type='int8')
results = {}
for i, l in enumerate(LINES, 1):
    wav = os.path.join(NAR_DIR, l['key'] + '.wav')
    if not os.path.exists(wav):
        print(l['key'], 'MISSING'); continue
    segs, _ = model.transcribe(wav, language='zh', beam_size=5, vad_filter=True)
    hyp = ''.join(s.text for s in segs)
    c = cer(l['text'], hyp)
    results[l['key']] = c
    mark = ' <<< BAD' if c > 0.35 else ''
    print('%s CER=%.3f%s  ASR: %s' % (l['key'], c, mark, hyp[:50]), flush=True)
json.dump(results, open(os.path.join(HERE, 'cer_last.json'), 'w'), ensure_ascii=False)
bad = [k for k, v in results.items() if v > 0.35]
print('BAD:', bad if bad else 'none')
