#ifndef VRRECORDER_NATIVE_SPOUT_CAPTURE_WORKER_HPP
#define VRRECORDER_NATIVE_SPOUT_CAPTURE_WORKER_HPP

#include <atomic>
#include <chrono>
#include <mutex>
#include <thread>

#include "spout_capture_pump.hpp"

namespace vrrecorder::native {

enum class SpoutCaptureWorkerResult {
    Aborted,
    SenderLost,
    InvalidState,
    Failed,
};

class SpoutCaptureWorkerPort {
public:
    virtual ~SpoutCaptureWorkerPort() = default;

    virtual vrrec_status_t Start(
        std::chrono::milliseconds poll_timeout) noexcept = 0;
    virtual void Abort() noexcept = 0;
    virtual SpoutCaptureWorkerResult Join() noexcept = 0;
};

class SpoutCaptureWorker final : public SpoutCaptureWorkerPort {
public:
    explicit SpoutCaptureWorker(SpoutCaptureSource &source) noexcept;
    ~SpoutCaptureWorker();

    SpoutCaptureWorker(const SpoutCaptureWorker &) = delete;
    SpoutCaptureWorker &operator=(const SpoutCaptureWorker &) = delete;

    vrrec_status_t Start(
        std::chrono::milliseconds poll_timeout) noexcept override;
    void Abort() noexcept override;
    SpoutCaptureWorkerResult Join() noexcept override;

private:
    void Run() noexcept;
    void SetResult(SpoutCaptureWorkerResult result) noexcept;
    void JoinThread() noexcept;

    SpoutCaptureSource &source_;
    std::mutex state_mutex_;
    std::mutex join_mutex_;
    std::thread thread_;
    std::chrono::milliseconds poll_timeout_ {0};
    SpoutCaptureWorkerResult result_ =
        SpoutCaptureWorkerResult::InvalidState;
    std::atomic_bool started_ = false;
    std::atomic_bool aborted_ = false;
    std::atomic_bool finished_ = false;
};

}

#endif
