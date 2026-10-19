#!/bin/sh
set -eu
source_root=$1
output=$2
case "$source_root$output" in
    *[[:space:]]*) printf '%s\n' 'Source and output paths must not contain whitespace.' >&2; exit 2 ;;
esac
fixture=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
mkdir -p "$output"
sofa="$source_root/SOFA/SOFA/src"
novas="$source_root/NOVAS31/NOVAS31"
sofa_sources=$(find "$sofa" -maxdepth 1 -name '*.c' ! -name 't_sofa_c.c' | sort)
# The guard above permits the original C source list to expand as separate arguments.
gcc -O2 '-D__declspec(x)=' -I "$sofa" -fPIC -shared $sofa_sources -Wl,-soname,libSOFA_2023_10_11.so -lm -o "$output/libSOFA_2023_10_11.so"
gcc -O2 '-D__declspec(x)=' -I "$sofa" "$sofa/t_sofa_c.c" -L "$output" -Wl,-rpath,'$ORIGIN' -l:libSOFA_2023_10_11.so -lm -o "$output/sofa-tests"
gcc -O2 '-D__declspec(x)=' -I "$novas" -fPIC -shared "$novas/novas.c" "$novas/novascon.c" "$novas/nutation.c" "$novas/eph_manager.c" "$novas/solsys1.c" "$novas/readeph0.c" -Wl,-soname,libNOVAS31.so -lm -o "$output/libNOVAS31.so"
gcc '-D__declspec(x)=' -I "$novas" -I "$sofa" "$fixture/native-abi.c" -o "$output/native-abi"
gcc -O2 '-D__declspec(x)=' -I "$novas" "$novas/checkout-stars-full.c" -L "$output" -Wl,-rpath,'$ORIGIN' -l:libNOVAS31.so -lm -o "$output/novas-full-checkout"
gcc -O2 '-D__declspec(x)=' -I "$novas" "$novas/checkout-stars.c" "$novas/novas.c" "$novas/novascon.c" "$novas/nutation.c" "$novas/solsys3.c" "$novas/readeph0.c" -lm -o "$output/novas-original-checkout"
gcc '-D__declspec(x)=' -I "$novas" "$fixture/native-ephem.c" -L "$output" -Wl,-rpath,'$ORIGIN' -l:libNOVAS31.so -o "$output/native-ephem"
"$output/sofa-tests" > "$output/sofa-tests.log"
"$output/native-abi" > "$output/native-abi.log"
"$output/novas-original-checkout" > "$output/novas-original-checkout.log"
nm -D --defined-only "$output/libSOFA_2023_10_11.so" > "$output/sofa-exports.txt"
nm -D --defined-only "$output/libNOVAS31.so" > "$output/novas-exports.txt"
gcc --version > "$output/compiler.txt"
