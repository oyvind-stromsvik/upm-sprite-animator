using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class SpriteAnimator : MonoBehaviour {
    public UnityEvent OnAnimationEndEvent { get; private set; } = new UnityEvent();

    public SpriteAnimation[] animations;

    // Set this to override the speed of the current animation.
    // Useful for having a dynamic fps based on move speed etc.
    public int fps;

    public bool looping;

    // Store this here because we can have a non-looping ping pong animation
    // and in that case we allow it to play once forwards and once in reverse
    // before we stop it.
    public bool pingPong;

    // If the current animation is set to ping pong and is now supposed to
    // be played in reverse.
    public bool reverse;

    // Set this to control the current frame of the animation. F. ex. setting
    // fps to 0 and this to 0 let's us show only the initial frame of the animation
    // for as long as we wish.
    public int currentFrame;

    public SpriteAnimation currentAnimation;

    private SpriteRenderer _spriteRenderer;
    private float _timer;

    private bool _playOnceUninterrupted;

    private void Awake() {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update() {
        // For enabling playing an uninterrupted animation like a weapon
        // attack even if we're telling the animator to play other animations
        // like walking, running or jumping at the same time.
        if (_playOnceUninterrupted) {
            return;
        }

        // Don't do anything if there is no animation assigned.
        if (currentAnimation == null) {
            return;
        }

        // Don't do anything if the current animation has no frames.
        if (currentAnimation.frames.Length == 0) {
            return;
        }

        // This is a really clever way of handling looping.
        // Taken from GameMaker, and inspired by Daniel Linssen's code.
        // This allows you to just increase the index of the frame to play and
        // it will still be capped to the available number of frames in the animation.
        currentFrame %= currentAnimation.frames.Length;

        // Set the current sprite.
        _spriteRenderer.sprite = currentAnimation.frames[currentFrame];

        if (ReachedEndOfAnimation()) {
            // The animation is not looping.
            if (!looping) {
                if (currentAnimation.showBlankFrameAtTheEnd) {
                    _spriteRenderer.sprite = null;
                }

                // If it's ping ponging let it loop once more before we stop it.
                if (pingPong) {
                    pingPong = false;
                    reverse = !reverse;
                }
                else {
                    Stop();
                }
            }
            // The current animation is set to ping pong so switch its direction.
            else if (pingPong) {
                reverse = !reverse;
            }
        }

        // If the framerate is zero don't calculate the next frame.
        // We can change the current frame from outside this script
        // and animate the sprite this way as well.
        if (fps == 0) {
            return;
        }

        IncreaseFrameCount();
    }

    private void IncreaseFrameCount() {
        _timer += Time.deltaTime;
        if (_timer >= (1f / fps)) {
            _timer = 0;
            if (reverse) {
                currentFrame--;
            }
            else {
                currentFrame++;
            }
        }
    }

    public void Play(string name, float speed = 1, bool reset = true) {
        if (currentAnimation != null && currentAnimation.name == name) {
            return;
        }

        bool found = false;
        foreach (SpriteAnimation animation in animations) {
            if (animation.name == name) {
                currentAnimation = animation;
                fps = Mathf.RoundToInt(Mathf.Abs(currentAnimation.fps * speed));
                looping = currentAnimation.looping;
                pingPong = currentAnimation.pingPong;
                reverse = speed < 0;

                if (reset) {
                    // Switch over to the new animation immediately. Otherwise
                    // there is a 1 frame delay.
                    currentFrame = reverse ? (currentAnimation.frames.Length - 1) : 0;
                    _spriteRenderer.sprite = currentAnimation.frames[currentFrame];
                }

                found = true;
                break;
            }
        }

        if (!found) {
            Debug.LogError("Animation " + name + " not found.");
        }
    }

    public void PlayOnceUninterrupted(string playOnceName) {
        StartCoroutine(DoPlayOnceUninterrupted(playOnceName));
    }

    private IEnumerator DoPlayOnceUninterrupted(string playOnceName) {
        _playOnceUninterrupted = true;

        SpriteAnimation playOnceAnimation = null;
        int playOnceCurrentFrame = 0;

        foreach (SpriteAnimation animation in animations) {
            if (animation.name == playOnceName) {
                playOnceAnimation = animation;
                playOnceCurrentFrame = 0;
                _spriteRenderer.sprite = playOnceAnimation.frames[playOnceCurrentFrame];
                break;
            }
        }

        if (playOnceAnimation == null) {
            yield break;
        }

        float t = 0;
        while (playOnceCurrentFrame < playOnceAnimation.frames.Length) {
            _spriteRenderer.sprite = playOnceAnimation.frames[playOnceCurrentFrame];
            t += Time.deltaTime;
            if (t >= 1f / playOnceAnimation.fps) {
                t = 0;
                playOnceCurrentFrame++;
            }

            yield return null;
        }

        _playOnceUninterrupted = false;
    }

    /// <summary>
    /// Returns the length of the animation in seconds.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    public float GetAnimationLength(string name) {
        if (currentAnimation != null && currentAnimation.name == name) {
            return currentAnimation.frames.Length * (1f / fps);
        }

        foreach (SpriteAnimation animation in animations) {
            if (animation.name == name) {
                return animation.frames.Length * (1f / fps);
            }
        }

        throw new NullReferenceException();
    }

    public bool ReachedEndOfAnimation() {
        if (!reverse && currentFrame == (currentAnimation.frames.Length - 1) && !looping) {
            OnAnimationEndEvent?.Invoke();
            return true;
        }

        if (reverse && currentFrame == 0) {
            OnAnimationEndEvent?.Invoke();
            return true;
        }

        return false;
    }

    public void Stop() {
        currentAnimation = null;
    }
}
