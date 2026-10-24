#include <cstdio>
#include <cmath>

extern "C" {
int optim_api_version();
int optim_algo_count();
int optim_testfn_count();
int optim_solve(int algo, int fn, int n, const double* x0, double* x_out,
                double* f_out, int* iter_out, double* err_out, int max_iter, int seed);
}

static int failures = 0;

static void check_point(const char* name, int rc, const double* x, double f,
                        int iter, double ax, double ay, double fmax)
{
    const double dx = std::fabs(x[0] - ax);
    const double dy = std::fabs(x[1] - ay);
    const bool ok = (rc == 0) && dx < 1e-4 && dy < 1e-4 && std::fabs(f) <= fmax;
    std::printf("%-24s rc=%d x=(%.6f, %.6f) f=%.6g iter=%d %s\n",
                name, rc, x[0], x[1], f, iter, ok ? "PASS" : "FAIL");
    if (!ok) failures++;
}

int main()
{
    double x[2], f, err;
    int it;

    std::printf("api=%d algos=%d fns=%d\n",
                optim_api_version(), optim_algo_count(), optim_testfn_count());
    if (optim_api_version() != 101 || optim_algo_count() != 13 || optim_testfn_count() != 15)
        failures++;

    x[0] = -1.2; x[1] = 1.0;
    int rc = optim_solve(0, 1, 2, x, x, &f, &it, &err, 2000, 42);
    check_point("bfgs rosenbrock", rc, x, f, it, 1.0, 1.0, 1e-8);

    x[0] = 1.0; x[1] = 1.0;
    rc = optim_solve(10, 10, 2, x, x, &f, &it, &err, 2000, 42);
    check_point("sumt constr1", rc, x, f, it, 5.0, 4.0, 1e-6);

    x[0] = 0.0; x[1] = 0.0;
    rc = optim_solve(11, 13, 2, x, x, &f, &it, &err, 2000, 42);
    std::printf("%-24s rc=%d x=(%.6f, %.6f) residual=%.3g iter=%d %s\n",
                "broyden root1", rc, x[0], x[1], f, it,
                (rc == 0 && std::fabs(x[0] - 0.3532) < 1e-3 &&
                 std::fabs(x[1] - 0.6061) < 1e-3) ? "PASS" : "FAIL");
    if (!(rc == 0 && std::fabs(x[0] - 0.3532) < 1e-3 && std::fabs(x[1] - 0.6061) < 1e-3))
        failures++;

    x[0] = 3.0; x[1] = 3.0;
    rc = optim_solve(8, 5, 2, x, x, &f, &it, &err, 500, 42);
    std::printf("%-24s rc=%d x=(%.6f, %.6f) f=%.6g iter=%d %s\n",
                "pso rastrigin", rc, x[0], x[1], f, it,
                (std::fabs(f) < 1e-3) ? "PASS" : "FAIL");
    if (!(std::fabs(f) < 1e-3)) failures++;

    std::printf(failures == 0 ? "=== ALL TESTS PASSED ===\n" : "=== FAILURES ===\n");
    return failures == 0 ? 0 : 1;
}
