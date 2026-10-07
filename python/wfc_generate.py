import argparse
import json
import math
import random
from collections import Counter

# Minimal CNN-like local model using only basic Python and math.
# Learns from2x2 windows: predicts bottom-right from (top-left, top, left).
# Input JSON: { "grids": [ grid, ... ] } where grid is grids[x][y]
# Output JSON: { "grids": [ generated_grid ] }

POS_TL =0
POS_T =1
POS_L =2
NUM_POS =3


class SimpleCNN:
 # One-layer softmax (multinomial logistic regression) over one-hot features
 # of3 neighbors. Parameters: W[C][C*NUM_POS], b[C].
 def __init__(self, classes):
 self.classes = list(classes) # actual tile ids
 self.C = len(self.classes)
 self.id_to_idx = {tid: i for i, tid in enumerate(self.classes)}
 self.W = [[0.0 for _ in range(self.C * NUM_POS)] for _ in range(self.C)]
 self.b = [0.0 for _ in range(self.C)]

 def _feature_index(self, pos_index, tile_idx):
 return pos_index * self.C + tile_idx

 def _softmax(self, scores):
 m = max(scores)
 exps = [math.exp(s - m) for s in scores]
 s = sum(exps)
 if s ==0.0:
 return [1.0 / len(scores) for _ in scores]
 inv =1.0 / s
 return [v * inv for v in exps]

 def predict_proba_from_context(self, tl_idx, t_idx, l_idx):
 feats = [
 self._feature_index(POS_TL, tl_idx),
 self._feature_index(POS_T, t_idx),
 self._feature_index(POS_L, l_idx),
 ]
 scores = [self.b[c] for c in range(self.C)]
 for c in range(self.C):
 wc = self.W[c]
 s = scores[c]
 for f in feats:
 s += wc[f]
 scores[c] = s
 return self._softmax(scores)

 def train(self, samples, epochs=5, lr=0.1, seed=None):
 # samples: list of tuples (tl_idx, t_idx, l_idx, target_idx)
 rng = random.Random(seed)
 for _ in range(epochs):
 rng.shuffle(samples)
 for (tl_idx, t_idx, l_idx, y_idx) in samples:
 probs = self.predict_proba_from_context(tl_idx, t_idx, l_idx)
 feats = [
 self._feature_index(POS_TL, tl_idx),
 self._feature_index(POS_T, t_idx),
 self._feature_index(POS_L, l_idx),
 ]
 for c in range(self.C):
 g = probs[c] - (1.0 if c == y_idx else0.0)
 # bias
 self.b[c] -= lr * g
 # weights (sparse update)
 wc = self.W[c]
 for f in feats:
 wc[f] -= lr * g

 def generate(self, W, H, seed=None):
 # Start grid with most-common class along row0 and col0; fill rest via model
 _ = random.Random(seed)
 default_idx =0
 grid = [[self.classes[default_idx] for _ in range(H)] for _ in range(W)]
 for y in range(H):
 for x in range(W):
 if x ==0 or y ==0:
 continue
 tl = grid[x -1][y -1]
 t = grid[x][y -1]
 l = grid[x -1][y]
 tl_idx = self.id_to_idx.get(tl,0)
 t_idx = self.id_to_idx.get(t,0)
 l_idx = self.id_to_idx.get(l,0)
 probs = self.predict_proba_from_context(tl_idx, t_idx, l_idx)
 best_c =0
 best_p = -1.0
 for c, p in enumerate(probs):
 if p > best_p:
 best_p = p
 best_c = c
 grid[x][y] = self.classes[best_c]
 return grid


def collect_classes(grids):
 counter = Counter()
 for g in grids:
 if not g:
 continue
 w = len(g)
 if w ==0:
 continue
 h = len(g[0])
 for x in range(w):
 for y in range(h):
 counter[g[x][y]] +=1
 if not counter:
 raise SystemExit("No tiles found in dataset")
 # Sort by frequency so index0 is the most common (used as default)
 classes = [tid for (tid, _) in counter.most_common()]
 return classes, counter


def build_training_samples(grids, id_to_idx):
 samples = []
 for g in grids:
 if not g:
 continue
 w = len(g)
 h = len(g[0]) if w >0 else0
 if w <2 or h <2:
 continue
 for x in range(1, w):
 for y in range(1, h):
 tl = g[x -1][y -1]
 t = g[x][y -1]
 l = g[x -1][y]
 yv = g[x][y]
 tl_idx = id_to_idx.get(tl,0)
 t_idx = id_to_idx.get(t,0)
 l_idx = id_to_idx.get(l,0)
 y_idx = id_to_idx.get(yv,0)
 samples.append((tl_idx, t_idx, l_idx, y_idx))
 if not samples:
 raise SystemExit("Not enough data to build training samples (need at least one2x2 window)")
 return samples


def main():
 ap = argparse.ArgumentParser()
 ap.add_argument('--input', required=True)
 ap.add_argument('--output', required=True)
 ap.add_argument('--width', type=int, required=True)
 ap.add_argument('--height', type=int, required=True)
 ap.add_argument('--seed', type=int, default=None)
 # compatibility: accepted but unused
 ap.add_argument('--temperature', type=float, default=1.0)
 # simple training controls
 ap.add_argument('--epochs', type=int, default=5)
 ap.add_argument('--lr', type=float, default=0.1)
 args = ap.parse_args()

 with open(args.input, 'r') as f:
 data = json.load(f)
 grids = data.get('grids', [])
 if not grids:
 raise SystemExit('Input JSON missing grids')

 classes, _ = collect_classes(grids)
 model = SimpleCNN(classes)
 samples = build_training_samples(grids, model.id_to_idx)

 model.train(samples, epochs=args.epochs, lr=args.lr, seed=args.seed)

 out_grid = model.generate(args.width, args.height, seed=args.seed)

 with open(args.output, 'w') as f:
 json.dump({'grids': [out_grid]}, f)


if __name__ == '__main__':
 main()
