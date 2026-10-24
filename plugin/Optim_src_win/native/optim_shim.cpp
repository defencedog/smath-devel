/*
 * optim_shim.cpp - C ABI around OptimLib (https://github.com/kthohr/optim).
 *
 * The shim exposes OptimLib's optimization, root-finding and constrained
 * solvers over a small set of built-in test functions. It exists because
 * OptimLib is header/source C++ with std::function callbacks, which a .NET
 * P/Invoke caller cannot supply; the objective is selected by id instead.
 *
 * Build: see build-native.ps1. Requires OptimLib headers/sources, its
 * BaseMatrixOps submodule and Eigen 3.4+ (header only).
 *
 * Licensed under the Apache License, Version 2.0 (same as OptimLib).
 */

#ifndef OPTIM_ENABLE_EIGEN_WRAPPERS
#define OPTIM_ENABLE_EIGEN_WRAPPERS
#endif
#ifndef OPTIM_DONT_USE_OPENMP
#define OPTIM_DONT_USE_OPENMP
#endif

#include "optim.hpp"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>
#include <string>
#include <vector>

#define OPTIM_SHIM_API 101

#if defined(_WIN32)
#define OPTIM_SHIM_EXPORT extern "C" __declspec(dllexport)
#else
#define OPTIM_SHIM_EXPORT extern "C" __attribute__((visibility("default")))
#endif

/*
 * Generic evaluation callback for caller-supplied problems.
 *
 * x        - current point, n doubles
 * out      - output buffer
 * out_len  - number of values requested (objective: 1; constraints: m+1;
 *            root system: m)
 * user     - opaque caller pointer
 *
 * Return 0 on success, non-zero on failure. A failed callback aborts the
 * solve and is reported through the normal return code.
 */
typedef int (*optim_eval_cb)(const double* x, int n, double* out, int out_len, void* user);

namespace optim_shim
{
    using optim::ColVec_t;
    using optim::Mat_t;
    using optim::fp_t;

    const double PI_LOCAL = 3.14159265358979;
    const double NAN_VALUE = std::numeric_limits<double>::quiet_NaN();

    // ---------------------------------------------------------------------
    // Test-function catalogue
    // ---------------------------------------------------------------------

    enum FnKind
    {
        FN_OBJECTIVE = 0,   // unconstrained minimization
        FN_CONSTRAINED = 1, // SUMT problem (objective + inequality constraints)
        FN_ROOT = 2         // system of nonlinear equations
    };

    struct FnDef
    {
        const char* name;
        int kind;
        int dim;            // 0 = dimension follows the caller's x0
        bool has_grad;
        bool bounded;
        double lb[4];
        double ub[4];
    };

    static const FnDef FNS[] =
    {
        // 1 - quadratic (Matlab fminunc example)
        { "quadratic", FN_OBJECTIVE, 2, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 2 - Rosenbrock
        { "rosenbrock", FN_OBJECTIVE, 2, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 3 - sphere, any dimension
        { "sphere", FN_OBJECTIVE, 0, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 4 - Beale
        { "beale", FN_OBJECTIVE, 2, true, true, { -4.5, -4.5, 0, 0 }, { 4.5, 4.5, 0, 0 } },
        // 5 - Booth
        { "booth", FN_OBJECTIVE, 2, true, true, { -10, -10, 0, 0 }, { 10, 10, 0, 0 } },
        // 6 - Rastrigin (A = 10), any dimension
        { "rastrigin", FN_OBJECTIVE, 0, false, true, { -5.12, -5.12, -5.12, -5.12 }, { 5.12, 5.12, 5.12, 5.12 } },
        // 7 - Ackley
        { "ackley", FN_OBJECTIVE, 2, false, true, { -5, -5, 0, 0 }, { 5, 5, 0, 0 } },
        // 8 - Levi (N. 13)
        { "levi", FN_OBJECTIVE, 2, false, true, { -10, -10, 0, 0 }, { 10, 10, 0, 0 } },
        // 9 - Bukin (N. 6)
        { "bukin", FN_OBJECTIVE, 2, false, true, { -15, -3, 0, 0 }, { -5, 3, 0, 0 } },
        // 10 - Holder table
        { "table", FN_OBJECTIVE, 2, false, true, { -10, -10, 0, 0 }, { 10, 10, 0, 0 } },
        // 11 - constrained: min (x1-5)^2+(x2-4)^2 s.t. -2x1-x2+14 <= 0
        { "constr_quadratic_1", FN_CONSTRAINED, 2, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 12 - constrained: same objective, two inequalities
        { "constr_quadratic_2", FN_CONSTRAINED, 2, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 13 - constrained Rosenbrock with cubic and line constraints
        { "constr_rosenbrock", FN_CONSTRAINED, 2, true, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 14 - root system (Matlab fsolve example)
        { "root_system_1", FN_ROOT, 2, false, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } },
        // 15 - root system
        { "root_system_2", FN_ROOT, 2, false, false, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } }
    };

    static const int FN_COUNT = static_cast<int>(sizeof(FNS) / sizeof(FNS[0]));

    enum AlgoKind
    {
        ALGO_MIN = 0,
        ALGO_CONSTRAINED = 1,
        ALGO_ROOT = 2
    };

    struct AlgoDef
    {
        const char* name;
        int kind;
    };

    static const AlgoDef ALGOS[] =
    {
        { "bfgs", ALGO_MIN },
        { "lbfgs", ALGO_MIN },
        { "cg", ALGO_MIN },
        { "gd", ALGO_MIN },
        { "newton", ALGO_MIN },
        { "nm", ALGO_MIN },
        { "de", ALGO_MIN },
        { "de_prmm", ALGO_MIN },
        { "pso", ALGO_MIN },
        { "pso_dv", ALGO_MIN },
        { "sumt", ALGO_CONSTRAINED },
        { "broyden", ALGO_ROOT },
        { "broyden_df", ALGO_ROOT }
    };

    static const int ALGO_COUNT = static_cast<int>(sizeof(ALGOS) / sizeof(ALGOS[0]));

    // ---------------------------------------------------------------------
    // Last error message (single-threaded host; overwritten per failure)
    // ---------------------------------------------------------------------

    static std::string g_last_error;

    static int fail(int code, const char* message)
    {
        g_last_error = message;
        return code;
    }

    // ---------------------------------------------------------------------
    // Objective functions
    // ---------------------------------------------------------------------

    static double obj_value(int fn, const ColVec_t& x)
    {
        if (fn == 1)
        {
            const double a = x(0), b = x(1);
            return 3 * a * a + 2 * a * b + b * b - 4 * a + 5 * b;
        }
        if (fn == 2)
        {
            const double a = x(0), b = x(1);
            const double t = b - a * a;
            return 100 * t * t + (1 - a) * (1 - a);
        }
        if (fn == 3)
        {
            return x.dot(x);
        }
        if (fn == 4)
        {
            const double x1 = x(0), x2 = x(1);
            const double x2sq = x2 * x2;
            const double x2cb = x2sq * x2;
            const double x1x2 = x1 * x2;
            return std::pow(1.5 - x1 + x1x2, 2)
                 + std::pow(2.25 - x1 + x1 * x2sq, 2)
                 + std::pow(2.625 - x1 + x1 * x2cb, 2);
        }
        if (fn == 5)
        {
            const double x1 = x(0), x2 = x(1);
            return std::pow(x1 + 2 * x2 - 7.0, 2) + std::pow(2 * x1 + x2 - 5.0, 2);
        }
        if (fn == 6)
        {
            const double A = 10.0;
            const int n = static_cast<int>(x.size());
            double sum = 0.0;
            for (int i = 0; i < n; i++)
                sum += x(i) * x(i) - A * std::cos(2 * PI_LOCAL * x(i));
            return A * n + sum;
        }
        if (fn == 7)
        {
            const double a = x(0), b = x(1);
            return 20 + std::exp(1.0)
                 - 20 * std::exp(-0.2 * std::sqrt(0.5 * (a * a + b * b)))
                 - std::exp(0.5 * (std::cos(2 * PI_LOCAL * a) + std::cos(2 * PI_LOCAL * b)));
        }
        if (fn == 8)
        {
            const double a = x(0), b = x(1);
            return std::pow(std::sin(3 * PI_LOCAL * a), 2)
                 + std::pow(a - 1, 2) * (1 + std::pow(std::sin(3 * PI_LOCAL * b), 2))
                 + std::pow(b - 1, 2) * (1 + std::pow(std::sin(2 * PI_LOCAL * a), 2));
        }
        if (fn == 9)
        {
            const double a = x(0), b = x(1);
            return 100 * std::sqrt(std::abs(b - 0.01 * a * a)) + 0.01 * std::abs(a + 10);
        }
        if (fn == 10)
        {
            const double a = x(0), b = x(1);
            return -std::abs(std::sin(a) * std::cos(b)
                 * std::exp(std::abs(1.0 - std::sqrt(a * a + b * b) / PI_LOCAL)));
        }
        if (fn == 11 || fn == 12)
        {
            const double a = x(0), b = x(1);
            return std::pow(a - 5.0, 2) + std::pow(b - 4.0, 2);
        }
        if (fn == 13)
        {
            const double a = x(0), b = x(1);
            return std::pow(1.0 - a, 2) + 100 * std::pow(b - a * a, 2);
        }
        return NAN_VALUE;
    }

    static bool obj_grad(int fn, const ColVec_t& x, ColVec_t& g)
    {
        if (fn == 1)
        {
            g(0) = 6 * x(0) + 2 * x(1) - 4;
            g(1) = 2 * x(0) + 2 * x(1) + 5;
            return true;
        }
        if (fn == 2 || fn == 13)
        {
            const double a = x(0), b = x(1);
            g(0) = -400 * (b - a * a) * a - 2 * (1 - a);
            g(1) = 200 * (b - a * a);
            return true;
        }
        if (fn == 3)
        {
            for (int i = 0; i < static_cast<int>(x.size()); i++)
                g(i) = 2.0 * x(i);
            return true;
        }
        if (fn == 4)
        {
            const double x1 = x(0), x2 = x(1);
            const double x1sq = x1 * x1;
            const double x2sq = x2 * x2;
            const double x2cb = x2sq * x2;
            const double x1x2 = x1 * x2;
            g(0) = (2 * x1 * x2cb * x2cb) + (2 * x1 * x2sq * x2sq) - (4 * x1 * x2cb)
                 - (2 * x1 * x2sq) - (4 * x1x2) + (6 * x1) + (5.25 * x2cb)
                 + (4.5 * x2sq) + (3 * x2) - 12.75;
            g(1) = (6 * x1sq * x2sq * x2cb) + (4 * x1sq * x2cb) - (6 * x1sq * x2sq)
                 - (2 * x1sq * x2) - (2 * x1sq) + (15.75 * x1 * x2sq)
                 + (9 * x1x2) + (3 * x1);
            return true;
        }
        if (fn == 5)
        {
            g(0) = 10 * x(0) + 8 * x(1) - 34;
            g(1) = 8 * x(0) + 10 * x(1) - 38;
            return true;
        }
        if (fn == 11 || fn == 12)
        {
            g(0) = 2.0 * (x(0) - 5.0);
            g(1) = 2.0 * (x(1) - 4.0);
            return true;
        }
        return false;
    }

    static void numerical_grad(int fn, const ColVec_t& x, ColVec_t& g)
    {
        const int n = static_cast<int>(x.size());
        ColVec_t xp = x;
        ColVec_t xm = x;
        for (int i = 0; i < n; i++)
        {
            const double h = 1e-6 * (1.0 + std::abs(x(i)));
            xp(i) = x(i) + h;
            xm(i) = x(i) - h;
            g(i) = (obj_value(fn, xp) - obj_value(fn, xm)) / (2.0 * h);
            xp(i) = x(i);
            xm(i) = x(i);
        }
    }

    static fp_t objective_cb(int fn, const ColVec_t& x, ColVec_t* grad_out)
    {
        if (grad_out)
        {
            if (!obj_grad(fn, x, *grad_out))
                numerical_grad(fn, x, *grad_out);
        }
        return static_cast<fp_t>(obj_value(fn, x));
    }

    static void numerical_hessian(int fn, const ColVec_t& x, Mat_t& hess)
    {
        const int n = static_cast<int>(x.size());
        hess.resize(n, n);

        ColVec_t xp, xm, xpp, xpm, xmp, xmm;
        for (int i = 0; i < n; i++)
        {
            const double hi = 1e-5 * (1.0 + std::abs(x(i)));

            // Diagonal: second difference. The four-point cross formula below
            // cannot be used when i == j (both perturbations hit the same
            // coordinate and cancel).
            xp = x; xm = x;
            xp(i) = x(i) + hi;
            xm(i) = x(i) - hi;
            hess(i, i) = (obj_value(fn, xp) - 2.0 * obj_value(fn, x)
                        + obj_value(fn, xm)) / (hi * hi);

            for (int j = i + 1; j < n; j++)
            {
                const double hj = 1e-5 * (1.0 + std::abs(x(j)));

                xpp = x; xpm = x; xmp = x; xmm = x;
                xpp(i) = x(i) + hi; xpp(j) = x(j) + hj;
                xpm(i) = x(i) + hi; xpm(j) = x(j) - hj;
                xmp(i) = x(i) - hi; xmp(j) = x(j) + hj;
                xmm(i) = x(i) - hi; xmm(j) = x(j) - hj;

                const double hij = (obj_value(fn, xpp) - obj_value(fn, xpm)
                                  - obj_value(fn, xmp) + obj_value(fn, xmm))
                                 / (4.0 * hi * hj);
                hess(i, j) = hij;
                hess(j, i) = hij;
            }
        }
    }

    // Newton's callback also supplies the Hessian.
    static fp_t objective_cb_hess(int fn, const ColVec_t& x, ColVec_t* grad_out, Mat_t* hess_out)
    {
        if (grad_out)
        {
            if (!obj_grad(fn, x, *grad_out))
                numerical_grad(fn, x, *grad_out);
        }
        if (hess_out)
            numerical_hessian(fn, x, *hess_out);
        return static_cast<fp_t>(obj_value(fn, x));
    }

    // ---------------------------------------------------------------------
    // Caller-supplied problems (generic callback)
    // ---------------------------------------------------------------------

    struct CustomState
    {
        optim_eval_cb cb;
        void* user;
        int kind;
        int m;
        bool error;
        std::string message;

        CustomState() : cb(NULL), user(NULL), kind(0), m(0), error(false) {}
    };

    static bool custom_call(CustomState& st, const ColVec_t& x, double* out, int out_len)
    {
        if (st.error) return false;
        if (st.cb == NULL)
        {
            st.error = true;
            st.message = "custom callback is null";
            return false;
        }

        const int rc = st.cb(x.data(), static_cast<int>(x.size()), out, out_len, st.user);
        if (rc != 0)
        {
            st.error = true;
            if (st.message.empty())
                st.message = "custom evaluation failed";
            return false;
        }
        return true;
    }

    static double custom_value(CustomState& st, const ColVec_t& x)
    {
        double value = NAN_VALUE;
        if (!custom_call(st, x, &value, 1))
            return NAN_VALUE;
        return value;
    }

    static void custom_grad(CustomState& st, const ColVec_t& x, ColVec_t& g)
    {
        const int n = static_cast<int>(x.size());
        g.resize(n);
        ColVec_t xp = x;
        ColVec_t xm = x;

        for (int i = 0; i < n; i++)
        {
            const double h = 1e-6 * (1.0 + std::abs(x(i)));
            xp(i) = x(i) + h;
            xm(i) = x(i) - h;

            const double vp = custom_value(st, xp);
            const double vm = custom_value(st, xm);
            g(i) = (vp - vm) / (2.0 * h);

            xp(i) = x(i);
            xm(i) = x(i);

            if (st.error)
            {
                g.setConstant(NAN_VALUE);
                return;
            }
        }
    }

    static fp_t custom_objective_cb(CustomState& st, const ColVec_t& x, ColVec_t* grad_out)
    {
        const double f = custom_value(st, x);
        if (grad_out)
            custom_grad(st, x, *grad_out);
        return static_cast<fp_t>(f);
    }

    static void custom_hessian(CustomState& st, const ColVec_t& x, Mat_t& hess)
    {
        const int n = static_cast<int>(x.size());
        hess.resize(n, n);

        ColVec_t xp, xm, xpp, xpm, xmp, xmm;
        for (int i = 0; i < n; i++)
        {
            const double hi = 1e-5 * (1.0 + std::abs(x(i)));

            xp = x;
            xm = x;
            xp(i) = x(i) + hi;
            xm(i) = x(i) - hi;

            const double f0 = custom_value(st, x);
            const double fp = custom_value(st, xp);
            const double fm = custom_value(st, xm);
            hess(i, i) = (fp - 2.0 * f0 + fm) / (hi * hi);

            for (int j = i + 1; j < n; j++)
            {
                const double hj = 1e-5 * (1.0 + std::abs(x(j)));

                xpp = x;
                xpm = x;
                xmp = x;
                xmm = x;
                xpp(i) = x(i) + hi;
                xpp(j) = x(j) + hj;
                xpm(i) = x(i) + hi;
                xpm(j) = x(j) - hj;
                xmp(i) = x(i) - hi;
                xmp(j) = x(j) + hj;
                xmm(i) = x(i) - hi;
                xmm(j) = x(j) - hj;

                const double fpp = custom_value(st, xpp);
                const double fpm = custom_value(st, xpm);
                const double fmp = custom_value(st, xmp);
                const double fmm = custom_value(st, xmm);
                const double hij = (fpp - fpm - fmp + fmm) / (4.0 * hi * hj);
                hess(i, j) = hij;
                hess(j, i) = hij;

                if (st.error)
                {
                    hess.setConstant(NAN_VALUE);
                    return;
                }
            }

            if (st.error)
            {
                hess.setConstant(NAN_VALUE);
                return;
            }
        }
    }

    static fp_t custom_objective_cb_hess(
        CustomState& st, const ColVec_t& x, ColVec_t* grad_out, Mat_t* hess_out)
    {
        const double f = custom_value(st, x);
        if (grad_out)
            custom_grad(st, x, *grad_out);
        if (hess_out)
            custom_hessian(st, x, *hess_out);
        return static_cast<fp_t>(f);
    }

    static ColVec_t custom_constraint_cb(CustomState& st, const ColVec_t& x, Mat_t* jacob_out)
    {
        const int m = st.m;
        const int n = static_cast<int>(x.size());
        std::vector<double> values(m + 1);
        ColVec_t c(m);

        if (!custom_call(st, x, &values[0], m + 1))
        {
            c.setConstant(NAN_VALUE);
            return c;
        }

        for (int i = 0; i < m; i++)
            c(i) = values[i + 1];

        if (jacob_out)
        {
            jacob_out->resize(m, n);
            ColVec_t xp = x;
            ColVec_t xm = x;
            std::vector<double> vp(m + 1);
            std::vector<double> vm(m + 1);

            for (int j = 0; j < n; j++)
            {
                const double h = 1e-6 * (1.0 + std::abs(x(j)));
                xp(j) = x(j) + h;
                xm(j) = x(j) - h;

                if (!custom_call(st, xp, &vp[0], m + 1) ||
                    !custom_call(st, xm, &vm[0], m + 1))
                {
                    jacob_out->setConstant(NAN_VALUE);
                    return c;
                }

                for (int i = 0; i < m; i++)
                    (*jacob_out)(i, j) = (vp[i + 1] - vm[i + 1]) / (2.0 * h);

                xp(j) = x(j);
                xm(j) = x(j);
            }
        }

        return c;
    }

    static ColVec_t custom_root_cb(CustomState& st, const ColVec_t& x)
    {
        const int m = st.m;
        std::vector<double> values(m);
        ColVec_t r(m);

        if (!custom_call(st, x, &values[0], m))
        {
            r.setConstant(NAN_VALUE);
            return r;
        }

        for (int i = 0; i < m; i++)
            r(i) = values[i];
        return r;
    }

    // ---------------------------------------------------------------------
    // Constraints (ids 11-13)
    // ---------------------------------------------------------------------

    static int constr_count(int fn)
    {
        if (fn == 11) return 1;
        if (fn == 12 || fn == 13) return 2;
        return 0;
    }

    static ColVec_t constr_values(int fn, const ColVec_t& x)
    {
        const double a = x(0), b = x(1);
        if (fn == 11)
        {
            ColVec_t c(1);
            c(0) = -2 * a - b + 14.0;
            return c;
        }
        if (fn == 12)
        {
            ColVec_t c(2);
            c(0) = -2 * a - b + 14.0;
            c(1) = a + b - 9.0;
            return c;
        }
        // fn == 13
        ColVec_t c(2);
        c(0) = std::pow(a - 1.0, 3) - b + 1.0;
        c(1) = a + b - 2.0;
        return c;
    }

    static void constr_jacobian(int fn, const ColVec_t& x, Mat_t& jac)
    {
        const double a = x(0);
        const int m = constr_count(fn);
        jac.resize(m, 2);
        if (fn == 11)
        {
            jac(0, 0) = -2.0; jac(0, 1) = -1.0;
        }
        else if (fn == 12)
        {
            jac(0, 0) = -2.0; jac(0, 1) = -1.0;
            jac(1, 0) = 1.0;  jac(1, 1) = 1.0;
        }
        else
        {
            jac(0, 0) = 3 * std::pow(a - 1.0, 2); jac(0, 1) = -1.0;
            jac(1, 0) = 1.0;                      jac(1, 1) = 1.0;
        }
    }

    static ColVec_t constr_cb(int fn, const ColVec_t& x, Mat_t* jacob_out)
    {
        if (jacob_out)
            constr_jacobian(fn, x, *jacob_out);
        return constr_values(fn, x);
    }

    // ---------------------------------------------------------------------
    // Root systems (ids 14-15)
    // ---------------------------------------------------------------------

    static ColVec_t root_values(int fn, const ColVec_t& x)
    {
        const double a = x(0), b = x(1);
        ColVec_t f(2);
        if (fn == 14)
        {
            f(0) = std::exp(-std::exp(-(a + b))) - b * (1 + a * a);
            f(1) = a * std::cos(b) + b * std::sin(a) - 0.5;
        }
        else
        {
            f(0) = 2 * a - b - std::exp(-a);
            f(1) = -a + 2 * b - std::exp(-b);
        }
        return f;
    }

    static fp_t root_cb(int fn, const ColVec_t& x, void*)
    {
        (void)fn; (void)x;
        return 0.0; // unused
    }

    // The root callbacks return a vector, so they need their own wrappers.

    // ---------------------------------------------------------------------
    // Bounds
    // ---------------------------------------------------------------------

    static void apply_bounds(const FnDef& def, int n, ColVec_t& lb, ColVec_t& ub)
    {
        lb.resize(n);
        ub.resize(n);
        for (int i = 0; i < n; i++)
        {
            const int k = (i < 4) ? i : 3;
            lb(i) = def.lb[k];
            ub(i) = def.ub[k];
        }
    }
}

using namespace optim_shim;

// -------------------------------------------------------------------------
// Public C ABI
// -------------------------------------------------------------------------

OPTIM_SHIM_EXPORT int optim_api_version()
{
    return OPTIM_SHIM_API;
}

OPTIM_SHIM_EXPORT const char* optim_library_name()
{
    return "OptimLib (kthohr/optim) with Eigen";
}

OPTIM_SHIM_EXPORT const char* optim_last_error()
{
    return g_last_error.c_str();
}

OPTIM_SHIM_EXPORT int optim_algo_count()
{
    return ALGO_COUNT;
}

OPTIM_SHIM_EXPORT const char* optim_algo_name(int id)
{
    if (id < 0 || id >= ALGO_COUNT) return "";
    return ALGOS[id].name;
}

OPTIM_SHIM_EXPORT int optim_algo_kind(int id)
{
    if (id < 0 || id >= ALGO_COUNT) return -1;
    return ALGOS[id].kind;
}

OPTIM_SHIM_EXPORT int optim_testfn_count()
{
    return FN_COUNT;
}

OPTIM_SHIM_EXPORT const char* optim_testfn_name(int id)
{
    if (id < 0 || id >= FN_COUNT) return "";
    return FNS[id].name;
}

OPTIM_SHIM_EXPORT int optim_testfn_kind(int id)
{
    if (id < 0 || id >= FN_COUNT) return -1;
    return FNS[id].kind;
}

OPTIM_SHIM_EXPORT int optim_testfn_dim(int id)
{
    if (id < 0 || id >= FN_COUNT) return -1;
    return FNS[id].dim;
}

OPTIM_SHIM_EXPORT int optim_testfn_bounded(int id)
{
    if (id < 0 || id >= FN_COUNT) return -1;
    return FNS[id].bounded ? 1 : 0;
}

OPTIM_SHIM_EXPORT double optim_testfn_lower(int id, int index)
{
    if (id < 0 || id >= FN_COUNT) return NAN_VALUE;
    const FnDef& d = FNS[id];
    if (!d.bounded) return NAN_VALUE;
    const int k = (index < 4) ? index : 3;
    return d.lb[k];
}

OPTIM_SHIM_EXPORT double optim_testfn_upper(int id, int index)
{
    if (id < 0 || id >= FN_COUNT) return NAN_VALUE;
    const FnDef& d = FNS[id];
    if (!d.bounded) return NAN_VALUE;
    const int k = (index < 4) ? index : 3;
    return d.ub[k];
}

OPTIM_SHIM_EXPORT double optim_testfn_value(int id, int n, const double* x)
{
    if (id < 0 || id >= FN_COUNT || x == NULL || n <= 0) return NAN_VALUE;
    const FnDef& d = FNS[id];
    if (d.kind == FN_ROOT) return NAN_VALUE;
    if (d.dim != 0 && n != d.dim) return NAN_VALUE;

    ColVec_t v(n);
    for (int i = 0; i < n; i++) v(i) = x[i];
    return obj_value(id + 1, v);
}

OPTIM_SHIM_EXPORT int optim_solve(
    int algo,
    int fn,
    int n,
    const double* x0,
    double* x_out,
    double* f_out,
    int* iter_out,
    double* err_out,
    int max_iter,
    int seed)
{
    g_last_error.clear();

    if (algo < 0 || algo >= ALGO_COUNT)
        return fail(2, "optim_solve: algorithm id out of range");
    if (fn < 0 || fn >= FN_COUNT)
        return fail(2, "optim_solve: test-function id out of range");
    if (n <= 0 || x0 == NULL || x_out == NULL)
        return fail(2, "optim_solve: null or empty input");

    const AlgoDef& ad = ALGOS[algo];
    const FnDef& fd = FNS[fn];

    if (fd.dim != 0 && n != fd.dim)
        return fail(2, "optim_solve: x0 length does not match the test function dimension");
    if (fd.kind != ad.kind)
        return fail(2, "optim_solve: algorithm and test function kinds do not match (use sumt for constrained problems and broyden/broyden_df for root systems)");

    try
    {
        ColVec_t x(n);
        for (int i = 0; i < n; i++) x(i) = x0[i];

        optim::algo_settings_t settings;
        settings.iter_max = (max_iter > 0) ? static_cast<size_t>(max_iter) : 2000;
        settings.rng_seed_value = (seed >= 0) ? static_cast<size_t>(seed) : 42;
        settings.print_level = 0;
        settings.conv_failure_switch = 0;

        // OptimLib leaves these output fields uninitialised, and some
        // algorithms (Newton's early return) never write them.
        settings.opt_iter = 0;
        settings.opt_fn_value = 0.0;
        settings.opt_error_value = 0.0;

        if (fd.bounded && algo >= 6 && algo <= 9) // de, de_prmm, pso, pso_dv
        {
            ColVec_t lb, ub;
            apply_bounds(fd, n, lb, ub);

            // DE/PSO budget is expressed in generations, not iterations.
            settings.de_settings.n_gen = settings.iter_max;
            settings.pso_settings.n_gen = settings.iter_max;

            // The population starts just inside the box, and the box itself is
            // enforced through vals_bound - matching the reference tests.
            settings.vals_bound = true;
            settings.lower_bounds = lb;
            settings.upper_bounds = ub;

            // Stochastic search settles on a plateau rather than meeting the
            // deterministic 1e-8 objective-change tolerance; a looser target
            // avoids reporting a perfectly good answer as a failure.
            settings.rel_objfn_change_tol = 1e-6;

            // The initial cloud is centred on the caller's guess (25% of the
            // box width), matching the reference tests' practice of seeding
            // the population near x0.
            ColVec_t init_lb = lb, init_ub = ub;
            for (int i = 0; i < n; i++)
            {
                const double width = ub(i) - lb(i);
                const double half  = 0.25 * width;
                double lo = std::max(lb(i), x(i) - half);
                double hi = std::min(ub(i), x(i) + half);

                if (hi - lo < 1e-8 * (1.0 + width))
                {
                    lo = std::max(lb(i), x(i) - 0.5 * width);
                    hi = std::min(ub(i), x(i) + 0.5 * width);
                }

                init_lb(i) = lo;
                init_ub(i) = hi;
            }
            settings.de_settings.initial_lb = init_lb;
            settings.de_settings.initial_ub = init_ub;
            settings.pso_settings.initial_lb = init_lb;
            settings.pso_settings.initial_ub = init_ub;
        }

        bool ok = false;
        const int fn_id = fn + 1;

        switch (algo)
        {
            case 0:  ok = optim::bfgs(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 1:  ok = optim::lbfgs(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 2:  ok = optim::cg(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 3:  ok = optim::gd(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 4:  ok = optim::newton(x, [fn_id](const ColVec_t& v, ColVec_t* g, Mat_t* h, void*) -> fp_t { return objective_cb_hess(fn_id, v, g, h); }, NULL, settings); break;
            case 5:  ok = optim::nm(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 6:  ok = optim::de(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 7:  ok = optim::de_prmm(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 8:  ok = optim::pso(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 9:  ok = optim::pso_dv(x, [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); }, NULL, settings); break;
            case 10:
                ok = optim::sumt(
                    x,
                    [fn_id](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return objective_cb(fn_id, v, g); },
                    NULL,
                    [fn_id](const ColVec_t& v, Mat_t* j, void*) -> ColVec_t { return constr_cb(fn_id, v, j); },
                    NULL,
                    settings);
                break;
            case 11:
                ok = optim::broyden(
                    x,
                    [fn_id](const ColVec_t& v, void*) -> ColVec_t { return root_values(fn_id, v); },
                    NULL,
                    settings);
                break;
            case 12:
                ok = optim::broyden_df(
                    x,
                    [fn_id](const ColVec_t& v, void*) -> ColVec_t { return root_values(fn_id, v); },
                    NULL,
                    settings);
                break;
            default:
                return fail(2, "optim_solve: unsupported algorithm");
        }

        for (int i = 0; i < n; i++) x_out[i] = x(i);

        double fval;
        if (fd.kind == FN_ROOT)
        {
            ColVec_t r = root_values(fn_id, x);
            fval = 0.0;
            for (int i = 0; i < static_cast<int>(r.size()); i++)
                fval = std::max(fval, std::abs(r(i)));
        }
        else
        {
            fval = obj_value(fn_id, x);
        }

        if (f_out) *f_out = fval;
        if (iter_out) *iter_out = static_cast<int>(settings.opt_iter);
        if (err_out) *err_out = settings.opt_error_value;

        if (!ok && fval != 0.0)
            g_last_error = "optim_solve: the algorithm reported failure";
        return ok ? 0 : 1;
    }
    catch (const std::exception& ex)
    {
        g_last_error = std::string("optim_solve: ") + ex.what();
        return 3;
    }
    catch (...)
    {
        g_last_error = "optim_solve: unknown native exception";
        return 3;
    }
}

OPTIM_SHIM_EXPORT int optim_solve_custom(
    int algo,
    int kind,
    int n,
    int m,
    const double* x0,
    double* x_out,
    double* f_out,
    int* iter_out,
    double* err_out,
    int max_iter,
    int seed,
    const double* lb,
    const double* ub,
    int bounded,
    optim_eval_cb eval,
    void* user)
{
    g_last_error.clear();

    if (algo < 0 || algo >= ALGO_COUNT)
        return fail(2, "optim_solve_custom: algorithm id out of range");
    if (kind < 0 || kind > 2)
        return fail(2, "optim_solve_custom: problem kind must be 0, 1 or 2");
    if (n <= 0 || x0 == NULL || x_out == NULL || eval == NULL)
        return fail(2, "optim_solve_custom: null or empty input");
    if (ALGOS[algo].kind != kind)
        return fail(2, "optim_solve_custom: algorithm and problem kinds do not match");

    if (kind == FN_OBJECTIVE)
    {
        if (m != 0)
            return fail(2, "optim_solve_custom: objective problems must pass m = 0");
    }
    else if (m <= 0)
    {
        return fail(2, "optim_solve_custom: constraints/residuals need m > 0");
    }

    if (kind == FN_ROOT && m != n)
    {
        return fail(2, "optim_solve_custom: root systems must be square (m == n)");
    }

    if (bounded && kind != FN_OBJECTIVE)
        return fail(2, "optim_solve_custom: bounds are only supported for objective problems");
    if (bounded && (algo < 6 || algo > 9))
        return fail(2, "optim_solve_custom: bounds are only supported by de/de_prmm/pso/pso_dv");
    if (bounded && (lb == NULL || ub == NULL))
        return fail(2, "optim_solve_custom: bounded problems need lower and upper arrays");
    if (!bounded && algo >= 6 && algo <= 9)
        return fail(2, "optim_solve_custom: de/de_prmm/pso/pso_dv require bounds");

    CustomState st;
    st.cb = eval;
    st.user = user;
    st.kind = kind;
    st.m = m;

    try
    {
        ColVec_t x(n);
        for (int i = 0; i < n; i++) x(i) = x0[i];

        optim::algo_settings_t settings;
        settings.iter_max = (max_iter > 0) ? static_cast<size_t>(max_iter) : 2000;
        settings.rng_seed_value = (seed >= 0) ? static_cast<size_t>(seed) : 42;
        settings.print_level = 0;
        settings.conv_failure_switch = 0;

        settings.opt_iter = 0;
        settings.opt_fn_value = 0.0;
        settings.opt_error_value = 0.0;

        if (bounded)
        {
            ColVec_t lbv(n), ubv(n);
            for (int i = 0; i < n; i++)
            {
                lbv(i) = lb[i];
                ubv(i) = ub[i];
                if (lbv(i) > ubv(i))
                    return fail(2, "optim_solve_custom: lower bound exceeds upper bound");
            }

            settings.vals_bound = true;
            settings.lower_bounds = lbv;
            settings.upper_bounds = ubv;

            settings.de_settings.n_gen = settings.iter_max;
            settings.pso_settings.n_gen = settings.iter_max;
            settings.rel_objfn_change_tol = 1e-6;

            ColVec_t init_lb = lbv, init_ub = ubv;
            for (int i = 0; i < n; i++)
            {
                const double width = ubv(i) - lbv(i);
                const double half = 0.25 * width;
                double lo = std::max(lbv(i), x(i) - half);
                double hi = std::min(ubv(i), x(i) + half);

                if (hi - lo < 1e-8 * (1.0 + width))
                {
                    lo = std::max(lbv(i), x(i) - 0.5 * width);
                    hi = std::min(ubv(i), x(i) + 0.5 * width);
                }

                init_lb(i) = lo;
                init_ub(i) = hi;
            }
            settings.de_settings.initial_lb = init_lb;
            settings.de_settings.initial_ub = init_ub;
            settings.pso_settings.initial_lb = init_lb;
            settings.pso_settings.initial_ub = init_ub;
        }

        bool ok = false;

        switch (algo)
        {
            case 0:
                ok = optim::bfgs(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 1:
                ok = optim::lbfgs(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 2:
                ok = optim::cg(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 3:
                ok = optim::gd(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 4:
                ok = optim::newton(x, [&st](const ColVec_t& v, ColVec_t* g, Mat_t* h, void*) -> fp_t { return custom_objective_cb_hess(st, v, g, h); }, NULL, settings);
                break;
            case 5:
                ok = optim::nm(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 6:
                ok = optim::de(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 7:
                ok = optim::de_prmm(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 8:
                ok = optim::pso(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 9:
                ok = optim::pso_dv(x, [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); }, NULL, settings);
                break;
            case 10:
                ok = optim::sumt(
                    x,
                    [&st](const ColVec_t& v, ColVec_t* g, void*) -> fp_t { return custom_objective_cb(st, v, g); },
                    NULL,
                    [&st](const ColVec_t& v, Mat_t* j, void*) -> ColVec_t { return custom_constraint_cb(st, v, j); },
                    NULL,
                    settings);
                break;
            case 11:
                ok = optim::broyden(
                    x,
                    [&st](const ColVec_t& v, void*) -> ColVec_t { return custom_root_cb(st, v); },
                    NULL,
                    settings);
                break;
            case 12:
                ok = optim::broyden_df(
                    x,
                    [&st](const ColVec_t& v, void*) -> ColVec_t { return custom_root_cb(st, v); },
                    NULL,
                    settings);
                break;
            default:
                return fail(2, "optim_solve_custom: unsupported algorithm");
        }

        if (st.error)
            return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());

        double fval = 0.0;
        if (kind == FN_OBJECTIVE)
        {
            fval = custom_value(st, x);
            if (st.error)
                return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());
        }
        else if (kind == FN_CONSTRAINED)
        {
            std::vector<double> values(m + 1);
            if (!custom_call(st, x, &values[0], m + 1))
                return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());
            fval = values[0];
        }
        else
        {
            std::vector<double> values(m);
            if (!custom_call(st, x, &values[0], m))
                return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());
            for (int i = 0; i < m; i++)
                fval = std::max(fval, std::abs(values[i]));
        }

        for (int i = 0; i < n; i++) x_out[i] = x(i);

        if (f_out) *f_out = fval;
        if (iter_out) *iter_out = static_cast<int>(settings.opt_iter);
        if (err_out) *err_out = settings.opt_error_value;

        if (!ok && fval != 0.0)
            g_last_error = "optim_solve_custom: the algorithm reported failure";
        return ok ? 0 : 1;
    }
    catch (const std::exception& ex)
    {
        if (st.error)
            return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());
        g_last_error = std::string("optim_solve_custom: ") + ex.what();
        return 3;
    }
    catch (...)
    {
        if (st.error)
            return fail(3, st.message.empty() ? "custom evaluation failed" : st.message.c_str());
        g_last_error = "optim_solve_custom: unknown native exception";
        return 3;
    }
}
