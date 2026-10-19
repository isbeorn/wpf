#include <stdio.h>
#include <stdlib.h>
#include "eph_manager.h"
int main(int argc, char **argv) {
    double begin = 0, end = 0;
    short number = 0;
    if (argc != 3) return 2;
    short status = ephem_open(argv[1], &begin, &end, &number);
    printf("open=%d\nbegin=%.17g\nend=%.17g\nde=%d\n", status, begin, end, number);
    if (status == 0) {
        short close_status = ephem_close();
        printf("close=%d\n", close_status);
        if (close_status != 0) return 3;
    }
    return status == atoi(argv[2]) ? 0 : 1;
}
