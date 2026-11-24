# Shared source list for bctts (sourced by build_win.sh / build_linux.sh).
# Paths are relative to the Flite source root (festvox/flite tag v2.2).
FLITE_SRCS="
src/audio/auclient.c src/audio/auserver.c src/audio/audio.c src/audio/au_streaming.c src/audio/au_none.c
src/cg/cst_cg.c src/cg/cst_mlsa.c src/cg/cst_mlpg.c src/cg/cst_vc.c src/cg/cst_cg_load_voice.c
src/cg/cst_cg_dump_voice.c src/cg/cst_cg_map.c src/cg/cst_spamf0.c
src/hrg/cst_utterance.c src/hrg/cst_relation.c src/hrg/cst_item.c src/hrg/cst_ffeature.c src/hrg/cst_rel_io.c
src/lexicon/cst_lexicon.c src/lexicon/cst_lts.c src/lexicon/cst_lts_rewrites.c
src/regex/cst_regex.c src/regex/regexp.c src/regex/regsub.c
src/speech/cst_wave.c src/speech/cst_wave_io.c src/speech/cst_track.c src/speech/cst_track_io.c
src/speech/cst_wave_utils.c src/speech/cst_lpcres.c src/speech/rateconv.c
src/speech/g721.c src/speech/g72x.c src/speech/g723_24.c src/speech/g723_40.c
src/stats/cst_cart.c src/stats/cst_viterbi.c src/stats/cst_ss.c
src/synth/cst_synth.c src/synth/cst_utt_utils.c src/synth/cst_voice.c src/synth/cst_phoneset.c
src/synth/cst_ffeatures.c src/synth/cst_ssml.c src/synth/flite.c
src/utils/cst_alloc.c src/utils/cst_error.c src/utils/cst_string.c src/utils/cst_wchar.c
src/utils/cst_tokenstream.c src/utils/cst_val.c src/utils/cst_features.c
src/utils/cst_endian.c src/utils/cst_socket.c src/utils/cst_val_const.c
src/utils/cst_val_user.c src/utils/cst_args.c src/utils/cst_url.c
src/utils/cst_mmap_none.c src/utils/cst_file_stdio.c
src/wavesynth/cst_units.c src/wavesynth/cst_clunits.c src/wavesynth/cst_diphone.c
src/wavesynth/cst_sigpr.c src/wavesynth/cst_sts.c src/wavesynth/cst_reflpc.c
lang/usenglish/us_int_accent_cart.c lang/usenglish/us_int_tone_cart.c lang/usenglish/us_f0_model.c
lang/usenglish/us_dur_stats.c lang/usenglish/us_durz_cart.c lang/usenglish/us_f0lr.c
lang/usenglish/us_phoneset.c lang/usenglish/us_ffeatures.c lang/usenglish/us_phrasing_cart.c
lang/usenglish/us_gpos.c lang/usenglish/us_text.c lang/usenglish/us_expand.c
lang/usenglish/us_nums_cart.c lang/usenglish/us_aswd.c lang/usenglish/usenglish.c lang/usenglish/us_pos_cart.c
lang/cmulex/cmu_lts_rules.c lang/cmulex/cmu_lts_model.c
lang/cmulex/cmu_lex.c lang/cmulex/cmu_lex_entries.c lang/cmulex/cmu_lex_data.c lang/cmulex/cmu_postlex.c
lang/cmu_us_kal16/cmu_us_kal16_diphone.c lang/cmu_us_kal16/cmu_us_kal16.c
lang/cmu_us_kal16/cmu_us_kal16_lpc.c lang/cmu_us_kal16/cmu_us_kal16_res.c lang/cmu_us_kal16/cmu_us_kal16_residx.c
"
FLITE_INCS="-Iinclude -Isrc/audio -Isrc/regex -Ilang/usenglish -Ilang/cmulex -Ilang/cmu_us_kal16"
FLITE_DEFS="-DCST_AUDIO_NONE -DCST_NO_SOCKETS"
