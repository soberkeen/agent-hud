# -*- coding: utf-8 -*-
"""差句多种子重试(SOP §4):对 BAD 句用 seeds [1,7,13,21] 重跑,取 CER 最低版
用法: python retry_seeds.py nar03 nar06 nar08"""
import json, os, re, subprocess, sys
from faster_whisper import WhisperModel

HERE = os.path.dirname(os.path.abspath(__file__))
GPK = r'I:\DSH 工作区\DSH剪辑\GPT-SoVITS'
NAR_DIR = os.path.join(HERE, '..', 'remotion', 'public', 'audio', 'nar')
SEEDS = [1, 7, 13, 21]

def norm(t):
    return re.sub(r'[\s，。；：、！？——\-.,;:!?()（）"\'"“”]', '', t).lower()

def cer(ref, hyp):
    r, h = norm(ref), norm(hyp)
    prev = list(range(len(h) + 1))
    for i, rc in enumerate(r, 1):
        cur = [i]
        for j, hc in enumerate(h, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (rc != hc)))
        prev = cur
    return prev[len(h)] / max(1, len(r))

LINES = {l['key']: l['text'] for l in json.load(open(os.path.join(HERE, 'nar_lines.json'), encoding='utf-8'))}
wm = WhisperModel('small', device='cpu', compute_type='int8')

def asr_cer(wav, ref):
    segs, _ = wm.transcribe(wav, language='zh', beam_size=5, vad_filter=True)
    hyp = ''.join(s.text for s in segs)
    return cer(ref, hyp), hyp

def convert(src, dst):
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', src,
                    '-af', 'silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.15,'
                           'areverse,silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.2,areverse,'
                           'loudnorm=I=-16:TP=-1.5:LRA=11',
                    '-ar', '44100', '-ac', '2', '-sample_fmt', 's16', dst], check=True)

for key in sys.argv[1:]:
    idx = int(key.replace('nar', ''))
    text = LINES[key]
    final = os.path.join(NAR_DIR, key + '.wav')
    best = (None, 999.0)
    for seed in SEEDS:
        tmp = os.path.join(HERE, 'dub_out', 'retry.wav')
        r = subprocess.run([os.path.join(GPK, 'runtime', 'python.exe'),
                            os.path.join(GPK, 'luotianyi-dub-kit', 'dub_cli.py'),
                            '--text', text, '--out', tmp, '--split', 'cut0',
                            '--speed', '1.1', '--seed', str(seed)],
                           capture_output=True, text=True, cwd=GPK, timeout=300)
        if not os.path.exists(tmp) or os.path.getsize(tmp) < 2000:
            print(key, 'seed', seed, 'synth fail'); continue
        convert(tmp, tmp + '.c.wav')
        c, hyp = asr_cer(tmp + '.c.wav', text)
        print('%s seed=%d CER=%.3f  %s' % (key, seed, c, hyp[:40]), flush=True)
        if c < best[1]:
            best = ((tmp + '.c.wav'), c)
        os.remove(tmp)
    if best[0]:
        convert(best[0], final)  # 统一转码(重跑 loudnorm 幅度一致)
        print('%s <- seed 择优 CER=%.3f' % (key, best[1]), flush=True)
print('DONE')
