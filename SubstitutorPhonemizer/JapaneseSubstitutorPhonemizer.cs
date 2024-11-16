using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace SubstitutorPhonemizer {
    [Phonemizer("Substitutor Japanese Phonemizer", "JA Sub", language: "JA")]
    public class SubstitutorJapanesePhonemizer : Phonemizer {
        /// <summary>
        /// The lookup table to convert a hiragana to its tail vowel.
        /// </summary>
        static readonly string[] vowels = new string[] {
            "a=ぁ,あ,か,が,さ,ざ,た,だ,な,は,ば,ぱ,ま,ゃ,や,ら,わ,ァ,ア,カ,ガ,サ,ザ,タ,ダ,ナ,ハ,バ,パ,マ,ャ,ヤ,ラ,ワ,a",
            "e=ぇ,え,け,げ,せ,ぜ,て,で,ね,へ,べ,ぺ,め,れ,ゑ,ェ,エ,ケ,ゲ,セ,ゼ,テ,デ,ネ,ヘ,ベ,ペ,メ,レ,ヱ,e",
            "i=ぃ,い,き,ぎ,し,じ,ち,ぢ,に,ひ,び,ぴ,み,り,ゐ,ィ,イ,キ,ギ,シ,ジ,チ,ヂ,ニ,ヒ,ビ,ピ,ミ,リ,ヰ,i",
            "o=ぉ,お,こ,ご,そ,ぞ,と,ど,の,ほ,ぼ,ぽ,も,ょ,よ,ろ,を,ォ,オ,コ,ゴ,ソ,ゾ,ト,ド,ノ,ホ,ボ,ポ,モ,ョ,ヨ,ロ,ヲ,o",
            "n=ん,n",
            "u=ぅ,う,く,ぐ,す,ず,つ,づ,ぬ,ふ,ぶ,ぷ,む,ゅ,ゆ,る,ゥ,ウ,ク,グ,ス,ズ,ツ,ヅ,ヌ,フ,ブ,プ,ム,ュ,ユ,ル,ヴ,u",
            "N=ン,ng"
        };

        static readonly string[] subs = new string [] {
            "あ=a",
            "え=e",
            "い=i",
            "お=o",
            "ん=n",
            "う=u"
        };

        static readonly string[] consonants = new string[] {
            "k=か,き,く,け,こ,きゃ,きゅ,きょ",
            "g=が,ぎ,ぐ,げ,ご,ぎゃ,ぎゅ,ぎょ",
            "s=さ,し,す,せ,そ,しゃ,しゅ,しぇ,しょ",
            "z=ざ,じ,ず,ぜ,ぞ,じゃ,じゅ,じぇ,じょ",
            "t=た,ち,つ,て,と,ちゃ,ちゅ,ちぇ,ちょ",
            "d=だ,ぢ,でぃ,づ,どぅ,で,ど,",
            "n=な,に,ぬ,ね,の,にゃ,にゅ,にぇ,にょ",
            "h=は,ひ,ふ,へ,ほ,ひゃ,ひゅ,ひぇ,ひょ",
            "b=ば,び,ぶ,べ,ぼ,びゃ,びゅ,びぇ,びょ",
            "p=ぱ,ぴ,ぷ,ぺ,ぽ,ぴゃ,ぴゅ,ぴぇ,ぴょ",
            "m=ま,み,む,め,も,みゃ,みゅ,みぇ,みょ",
            "y=や,ゆ,いぇ,よ",
            "r=ら,り,る,れ,ろ,りゃ,りゅ,りぇ,りょ",
            "w=わ,うぃ,うぇ,を"
        };

        static readonly Dictionary<string, string> vowelLookup;
        static readonly Dictionary<string, string> consonantLookup;

        static readonly Dictionary<string, string> subLookup;

        static SubstitutorJapanesePhonemizer() {
            // Converts the lookup table from raw strings to a dictionary for better performance.
            vowelLookup = vowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);

            consonantLookup = consonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split('.').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);

                
            subLookup = subs.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split('.').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        private USinger singer;

        // Simply stores the singer in a field.
        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize(); //measures for Unicode

            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                // If a hint is present, returns the hint.
                if (CheckOtoUntilHit(new string[] { note.phoneticHint.Normalize() }, note, out var ph)) {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme {
                                phoneme = ph.Alias,
                            }
                        },
                    };
                }
            }

            string[] tests = new string[] { $"- {currentLyric}", currentLyric };
            //Check if current lyric is valid
            if (!CheckOtoUntilHit(tests, note, out var oto))
            {
                var unicode = ToUnicodeElements(currentLyric);
                if (vowelLookup.TryGetValue(unicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    unicode = ToUnicodeElements(vow);
                    if (subLookup.TryGetValue(unicode.LastOrDefault() ?? string.Empty, out var newNote)) {
                        currentLyric = newNote.Normalize();
                        tests = new string[] { $"- {currentLyric}", currentLyric };
                    }
                }
            }

            // From VCV phonemizer
            // The alias for no previous neighbour note. For example, "- な" for "な".
            bool hasVCV = false;
            string vowel = "";
            if (prevNeighbour != null) {
                // If there is a previous neighbour note, first get its hint or lyric.
                var prevLyric = prevNeighbour.Value.lyric.Normalize();
                if (!string.IsNullOrEmpty(prevNeighbour.Value.phoneticHint)) {
                    prevLyric = prevNeighbour.Value.phoneticHint.Normalize();
                }
                // Get the last unicode element of the hint or lyric. For example, "ゃ" from "きゃ" or "- きゃ".
                var unicode = ToUnicodeElements(prevLyric);
                // Look up the trailing vowel. For example "a" for "ゃ".
                if (vowelLookup.TryGetValue(unicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    // Now replace "- な" initially set to "a な".
                    tests = new string[] { $"{vow} {currentLyric}", $"* {currentLyric}", currentLyric, $"- {currentLyric}" };
                    hasVCV = true;
                    vowel = vow;
                }
            }

            if (CheckOtoUntilHit(tests, note, out oto)) {
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme {
                            phoneme = oto.Alias,
                        }
                    },
                };
            }
            
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = currentLyric,
                    }
                },
            };
        }

        private bool CheckOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string color = attr.voiceColor ?? "";

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, color, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, color, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            if (otos.Count > 0) {
                if (otos.Any(oto => (oto.Color ?? string.Empty) == color)) {
                    oto = otos.Find(oto => (oto.Color ?? string.Empty) == color);
                    return true;
                } else {
                    oto = otos.First();
                    return true;
                }
            }
            return false;
        }
    }
}
