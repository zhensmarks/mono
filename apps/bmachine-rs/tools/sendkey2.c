// Alat uji keyboard untuk BMachine Slint di Xvfb tanpa WM.
// Kirim key via XSendEvent ke window app terbesar + XSetInputFocus dulu
// (tanpa WM, window tidak pernah menerima fokus keyboard sendiri).
// Pakai: sendkey2 <display> <mode> [teks]
//   mode: ctrll | esc | ctrlcomma | ctrlf | ctrla | ctrlc | ctrlv | ctrlz
//         | ctrlshift1..5 | f2 | delete | return | altleft | altright | altup
//         | type <teks>
// Build: gcc -o /tmp/sendkey2 tools/sendkey2.c -lX11
#include <stdio.h>
#include <string.h>
#include <unistd.h>
#include <X11/Xlib.h>
#include <X11/keysym.h>

static Display *dpy;
static Window win, rootw;

static void send_key(KeyCode kc, int press, unsigned int state) {
    XKeyEvent ev;
    ev.type = press ? KeyPress : KeyRelease;
    ev.serial = 0;
    ev.send_event = True;
    ev.display = dpy;
    ev.window = win;
    ev.root = rootw;
    ev.subwindow = None;
    ev.time = CurrentTime;
    ev.x = 0; ev.y = 0; ev.x_root = 0; ev.y_root = 0;
    ev.state = state;
    ev.keycode = kc;
    ev.same_screen = True;
    XSendEvent(dpy, win, True, KeyPressMask | KeyReleaseMask, (XEvent *)&ev);
    XFlush(dpy);
}

static void tap(KeySym ks, unsigned int state) {
    KeyCode kc = XKeysymToKeycode(dpy, ks);
    send_key(kc, 1, state);
    usleep(60000);
    send_key(kc, 0, state);
    usleep(60000);
}

static void tap_mod(KeySym mod, KeySym ks) {
    KeyCode kmod = XKeysymToKeycode(dpy, mod);
    unsigned int state = mod == XK_Control_L ? ControlMask : mod == XK_Alt_L ? Mod1Mask : ShiftMask;
    send_key(kmod, 1, 0);
    usleep(50000);
    tap(ks, state);
    send_key(kmod, 0, 0);
    usleep(80000);
}

static Window find_app(Window parent, int depth) {
    Window root_ret, parent_ret, *children = NULL;
    unsigned int n = 0;
    if (!XQueryTree(dpy, parent, &root_ret, &parent_ret, &children, &n)) return 0;
    Window best = 0;
    long best_area = 0;
    for (unsigned int i = 0; i < n; i++) {
        XWindowAttributes a;
        if (!XGetWindowAttributes(dpy, children[i], &a)) continue;
        if (a.class == InputOutput && a.map_state == IsViewable) {
            long area = (long)a.width * a.height;
            if (area > best_area) { best_area = area; best = children[i]; }
        }
        if (depth < 2) {
            Window sub = find_app(children[i], depth + 1);
            if (sub) {
                XWindowAttributes sa;
                if (XGetWindowAttributes(dpy, sub, &sa)) {
                    long area = (long)sa.width * sa.height;
                    if (area > best_area) { best_area = area; best = sub; }
                }
            }
        }
    }
    if (children) XFree(children);
    return best;
}

int main(int argc, char **argv) {
    const char *disp = argc > 1 ? argv[1] : ":99";
    const char *mode = argc > 2 ? argv[2] : "ctrll";
    dpy = XOpenDisplay(disp);
    if (!dpy) { printf("GAGAL buka %s\n", disp); return 1; }
    rootw = DefaultRootWindow(dpy);
    win = find_app(rootw, 0);
    if (!win) { printf("window app tidak ditemukan\n"); return 1; }
    XSetInputFocus(dpy, win, RevertToParent, CurrentTime);
    XFlush(dpy);
    usleep(300000);

    if (strcmp(mode, "esc") == 0) tap(XK_Escape, 0);
    else if (strcmp(mode, "ctrlcomma") == 0) tap_mod(XK_Control_L, XK_comma);
    else if (strcmp(mode, "ctrll") == 0) tap_mod(XK_Control_L, XK_l);
    else if (strcmp(mode, "ctrlf") == 0) tap_mod(XK_Control_L, XK_f);
    else if (strcmp(mode, "ctrla") == 0) tap_mod(XK_Control_L, XK_a);
    else if (strcmp(mode, "ctrlc") == 0) tap_mod(XK_Control_L, XK_c);
    else if (strcmp(mode, "ctrlv") == 0) tap_mod(XK_Control_L, XK_v);
    else if (strcmp(mode, "ctrlx") == 0) tap_mod(XK_Control_L, XK_x);
    else if (strcmp(mode, "ctrlz") == 0) tap_mod(XK_Control_L, XK_z);
    else if (strcmp(mode, "f2") == 0) tap(XK_F2, 0);
    else if (strcmp(mode, "delete") == 0) tap(XK_Delete, 0);
    else if (strcmp(mode, "return") == 0) tap(XK_Return, 0);
    else if (strcmp(mode, "altleft") == 0) tap_mod(XK_Alt_L, XK_Left);
    else if (strcmp(mode, "altright") == 0) tap_mod(XK_Alt_L, XK_Right);
    else if (strcmp(mode, "altup") == 0) tap_mod(XK_Alt_L, XK_Up);
    else if (strncmp(mode, "ctrlshift", 9) == 0 && mode[9] >= '1' && mode[9] <= '5') {
        KeyCode kctrl = XKeysymToKeycode(dpy, XK_Control_L);
        KeyCode kshift = XKeysymToKeycode(dpy, XK_Shift_L);
        KeySym ks = XK_1 + (mode[9] - '1');
        send_key(kctrl, 1, 0); usleep(50000);
        send_key(kshift, 1, ControlMask); usleep(50000);
        tap(ks, ControlMask | ShiftMask);
        send_key(kshift, 0, ControlMask); usleep(50000);
        send_key(kctrl, 0, 0); usleep(80000);
    }
    else if (strcmp(mode, "type") == 0 && argc > 3) {
        const char *t = argv[3];
        for (int i = 0; t[i]; i++) {
            char buf[2] = { t[i], 0 };
            KeySym ks = XStringToKeysym(buf);
            if (ks == NoSymbol) { printf("skip char %c\n", t[i]); continue; }
            tap(ks, 0);
        }
    }
    else { printf("mode tidak dikenal: %s\n", mode); return 1; }
    printf("%s terkirim ke 0x%lx\n", mode, win);
    XCloseDisplay(dpy);
    return 0;
}
